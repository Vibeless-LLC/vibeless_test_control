using Gridline.Core.Addressing;
using Gridline.Core.Commands;
using Gridline.Core.Dependencies;
using Gridline.Core.Evaluation;
using Gridline.Core.Functions;
using Gridline.Core.Parsing;

namespace Gridline.Core;

/// <summary>
/// A single-sheet workbook (§9.1). Stores cell inputs (§4.3), keeps the static dependency graph (§6.1) and
/// recalculates incrementally and synchronously on every mutation (§6.7, §9.2). Not thread-safe.
/// </summary>
public sealed class Workbook
{
    public const int MaxRows = 100_000;
    public const int MaxColumns = 702;
    public const int MaxInputLength = 32_767;
    public const int UndoLimit = 100;

    private static readonly CellValue Circ = CellValue.FromError(ErrorCodes.Circ);

    /// <summary>
    /// A defined name (§9.6): its defined casing, canonical <c>refersTo</c>, and target (null when
    /// <c>refersTo</c> is <c>#REF!</c>).
    /// </summary>
    private readonly record struct NameDef(string Name, string RefersTo, RangeAddress? Target);

    private readonly CellStore _store = new();
    private readonly DependencyGraph _graph = new();
    private readonly RecalcPlanner _planner;
    private readonly FunctionArityAdapter _arity;
    private readonly Evaluator _evaluator;
    private readonly EvalContext _ctx;
    private readonly Func<int, bool> _isFormula;
    private readonly Dictionary<string, NameDef> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly UndoHistory _history = new(UndoLimit);
    private long _lastStepId;

    public Workbook()
    {
        var registry = FunctionRegistry.CreateDefault();
        _arity = new FunctionArityAdapter(registry);
        _evaluator = new Evaluator(registry.Functions);
        _planner = new RecalcPlanner(_graph);
        _ctx = new EvalContext(_store, _names);
        _isFormula = IsFormula;
    }

    /// <summary>Statistics of the most recent mutating call or load (§9.2).</summary>
    public RecalcStats LastRecalc { get; private set; }

    /// <summary>Raised once per mutating call, after recalc, when any input or value changed (§9.2).</summary>
    public event EventHandler<CellsChangedEventArgs>? CellsChanged;

    /// <summary>
    /// Builds a workbook from cell inputs and defined names, then runs a full recalc (§6.7 "Load"). No event is
    /// raised. Throws <see cref="ArgumentException"/> on an invalid or duplicate (case-insensitive) address, an input
    /// over <see cref="MaxInputLength"/>, an invalid or duplicate (case-insensitive) name, or an invalid
    /// <c>refersTo</c> (§9.6: a fully absolute cell or range, or <c>#REF!</c>).
    /// </summary>
    public static Workbook FromSnapshot(IReadOnlyList<KeyValuePair<string, string>> cells, IReadOnlyList<KeyValuePair<string, string>> names)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(names);

        // Validate everything before building anything.
        var defs = new NameDef[names.Count];
        var seenNames = new HashSet<string>(names.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Count; i++)
        {
            var (name, refersTo) = names[i];
            defs[i] = ParseName(name, refersTo, nameof(names));
            if (!seenNames.Add(name)) throw new ArgumentException($"Duplicate name: '{name}'", nameof(names));
        }

        var keys = new int[cells.Count];
        var seen = new HashSet<int>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            var (address, input) = cells[i];
            int key = ParseKey(address, nameof(cells));
            ValidateInput(input, nameof(cells));
            if (!seen.Add(key)) throw new ArgumentException($"Duplicate cell address: '{address}'", nameof(cells));
            keys[i] = key;
        }

        var wb = new Workbook();
        foreach (var d in defs) wb.AddName(d);
        var formulaKeys = new HashSet<int>();
        for (int i = 0; i < cells.Count; i++)
        {
            wb.Apply(keys[i], cells[i].Value);
            if (wb.IsFormula(keys[i])) formulaKeys.Add(keys[i]);
        }
        var plan = wb._planner.PlanAll(formulaKeys);
        wb.LastRecalc = new RecalcStats(wb.Evaluate(plan, valueChanged: null));
        return wb;
    }

    /// <summary>
    /// Sets a cell's input; <c>""</c> clears it. Recalculates its dependents before returning. One undo step, unless
    /// the input is unchanged (then no step, no event, not dirty; the cell and its dependents are still re-evaluated).
    /// </summary>
    public void SetCell(string address, string input)
    {
        int key = ParseKey(address, nameof(address));
        ValidateInput(input, nameof(input));
        string old = CurrentInput(key);
        if (string.Equals(old, input, StringComparison.Ordinal))
        {
            // Phase 1 behaviour kept (§9.2): {X} ∪ dependents(X) are re-evaluated; values cannot change.
            var valueChanged = new List<int>();
            LastRecalc = new RecalcStats(Evaluate(_planner.Plan([key], [], _isFormula), valueChanged));
            Raise(valueChanged);
            return;
        }
        Commit([new CellEdit(key, old, input)], newNames: null, record: true);
    }

    /// <summary>
    /// Applies several inputs as one mutation and one undo step (§9.3). Addresses are case-insensitive and the last
    /// edit of an address wins. Every address and input is validated before anything is applied. A batch that
    /// changes no input (including an empty batch) creates no step.
    /// </summary>
    public void ApplyBatch(IReadOnlyList<KeyValuePair<string, string>> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var keys = new int[edits.Count];
        for (int i = 0; i < edits.Count; i++)
        {
            keys[i] = ParseKey(edits[i].Key, nameof(edits));
            ValidateInput(edits[i].Value, nameof(edits));
        }

        // Last wins; each key keeps the position of its first occurrence. OldInput is the pre-commit input.
        var slot = new Dictionary<int, int>(edits.Count);
        var list = new List<CellEdit>(edits.Count);
        for (int i = 0; i < edits.Count; i++)
        {
            var e = new CellEdit(keys[i], CurrentInput(keys[i]), edits[i].Value);
            if (slot.TryGetValue(keys[i], out int j)) list[j] = e;
            else { slot[keys[i]] = list.Count; list.Add(e); }
        }
        Commit(list, newNames: null, record: true);
    }

    /// <summary>
    /// Copies a cell or range to the same-sized rectangle at <paramref name="destinationTopLeft"/> as one undo step
    /// (§9.4). The source is snapshotted first, so overlap is safe. Blank cells clear their destination; non-formula
    /// inputs and formulas that are parse errors are copied verbatim; other formulas have their references shifted
    /// by the offset and are re-emitted canonically (§5.5), even at offset zero. Throws
    /// <see cref="ArgumentException"/> before changing anything if an address is invalid or the destination
    /// rectangle exceeds the grid.
    /// </summary>
    public void CopyPaste(string sourceRange, string destinationTopLeft)
    {
        if (!RangeAddress.TryParse(sourceRange, out var src))
            throw new ArgumentException($"Invalid range: '{sourceRange}'", nameof(sourceRange));
        if (!CellAddress.TryParse(destinationTopLeft, out var dst))
            throw new ArgumentException($"Invalid cell address: '{destinationTopLeft}'", nameof(destinationTopLeft));
        int rows = src.Rows, cols = src.Cols;
        if ((long)dst.Row + rows - 1 > MaxRows || (long)dst.Col + cols - 1 > MaxColumns)
            throw new ArgumentException("Destination extends beyond the grid.", nameof(destinationTopLeft));

        int dRow = dst.Row - src.TopLeft.Row, dCol = dst.Col - src.TopLeft.Col;
        var pasted = new string[rows * cols];
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            int key = new CellAddress(src.TopLeft.Row + r, src.TopLeft.Col + c).Key;
            string text = "";
            if (_store.TryGet(key, out var cell))
            {
                text = cell.Input;
                if (cell.Kind == CellKind.Formula && cell.Formula is { IsParseError: false, Root: { } root })
                    text = FormulaPrinter.Print(ReferenceRewriter.Shift(root, dRow, dCol, out _), NameCasing);
            }
            // Re-emitted formulas can grow (shifted refs get longer, #REF!); keep the input-length invariant.
            if (text.Length > MaxInputLength)
                throw new ArgumentException($"Pasted input exceeds {MaxInputLength} characters.", nameof(destinationTopLeft));
            pasted[r * cols + c] = text;
        }

        var edits = new List<CellEdit>(pasted.Length);
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            int key = new CellAddress(dst.Row + r, dst.Col + c).Key;
            edits.Add(new CellEdit(key, CurrentInput(key), pasted[r * cols + c]));
        }
        Commit(edits, newNames: null, record: true);
    }

    private string? NameCasing(string upper) => _names.TryGetValue(upper, out var d) ? d.Name : null;

    /// <summary>True when there is a step to undo (§9.3).</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>True when there is an undone step to redo (§9.3).</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>True when the history position differs from the one recorded by <see cref="MarkSaved"/> (§9.3).</summary>
    public bool IsDirty => _history.IsDirty;

    /// <summary>Records the current history position as saved (§9.3).</summary>
    public void MarkSaved() => _history.MarkSaved();

    /// <summary>Restores the exact inputs and names from before the most recent step; false if there is none.</summary>
    public bool Undo()
    {
        if (!_history.TryUndo(out var step)) return false;
        var edits = new CellEdit[step.Cells.Count];
        for (int i = 0; i < edits.Length; i++)
            edits[i] = new CellEdit(step.Cells[i].Key, CurrentInput(step.Cells[i].Key), step.Cells[i].OldInput);
        Commit(edits, step.NamesBefore, record: false);
        return true;
    }

    /// <summary>Reapplies the most recently undone step; false if there is none.</summary>
    public bool Redo()
    {
        if (!_history.TryRedo(out var step)) return false;
        var edits = new CellEdit[step.Cells.Count];
        for (int i = 0; i < edits.Length; i++)
            edits[i] = new CellEdit(step.Cells[i].Key, CurrentInput(step.Cells[i].Key), step.Cells[i].NewInput);
        Commit(edits, step.NamesAfter, record: false);
        return true;
    }

    /// <summary>
    /// Replaces all defined names as one mutation and one undo step (none if identical, casing included). Validates
    /// every name and <c>refersTo</c> (§9.6) and rejects case-insensitive duplicates before changing anything.
    /// Phase 3 <c>DefineName</c>/<c>DeleteName</c> route through here.
    /// </summary>
    internal void ReplaceNames(IReadOnlyList<KeyValuePair<string, string>> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var canonical = new KeyValuePair<string, string>[names.Count];
        var seen = new HashSet<string>(names.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Count; i++)
        {
            var d = ParseName(names[i].Key, names[i].Value, nameof(names));
            if (!seen.Add(d.Name)) throw new ArgumentException($"Duplicate name: '{d.Name}'", nameof(names));
            canonical[i] = new(d.Name, d.RefersTo);
        }
        Array.Sort(canonical, static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
        Commit([], canonical, record: true);
    }

    /// <summary>The exact input of the cell, or <c>""</c> if blank.</summary>
    public string GetInput(string address) =>
        _store.TryGet(ParseKey(address, nameof(address)), out var c) ? c.Input : "";

    /// <summary>The cell's current value; Blank for an empty cell.</summary>
    public CellValue GetValue(string address) =>
        _store.TryGet(ParseKey(address, nameof(address)), out var c) ? c.Value : CellValue.Blank;

    /// <summary>Canonical addresses of all cells with input, row-major.</summary>
    public IReadOnlyList<string> GetUsedAddresses()
    {
        var result = new List<string>(_store.Count);
        foreach (int key in _store.KeysRowMajor()) result.Add(CellAddress.FromKey(key).ToString());
        return result;
    }

    /// <summary>
    /// Defined names as (name in its defined casing, canonical <c>refersTo</c>), sorted
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> (§9.1, §9.6).
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> GetNames()
    {
        var result = new KeyValuePair<string, string>[_names.Count];
        int i = 0;
        foreach (var d in _names.Values) result[i++] = new(d.Name, d.RefersTo);
        Array.Sort(result, static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
        return result;
    }

    /// <summary>Validates one name and its <c>refersTo</c> (§9.6) and returns its canonical definition.</summary>
    private static NameDef ParseName(string name, string refersTo, string paramName)
    {
        if (!NameRules.IsValidName(name)) throw new ArgumentException($"Invalid name: '{name}'", paramName);
        if (refersTo == ErrorCodes.Ref) return new NameDef(name, ErrorCodes.Ref, null);
        if (!NameRules.TryParseRefersTo(refersTo, out var target))
            throw new ArgumentException($"Invalid refersTo for name '{name}': '{refersTo}'", paramName);
        return new NameDef(name, NameRules.FormatRefersTo(target), target);
    }

    /// <summary>Stores a (validated) name and points its dependency-graph node at the target (§6.1, §9.6).</summary>
    private void AddName(NameDef d)
    {
        _names[d.Name] = d;
        _graph.SetNameTarget(d.Name.ToUpperInvariant(), d.Target);
    }

    /// <summary>
    /// The single mutation path (§9.2, §9.3). <paramref name="edits"/> are validated, have distinct keys and carry the
    /// current input as <c>OldInput</c>; no-op edits are dropped. <paramref name="newNames"/> is the complete sorted
    /// name list to install (validated, canonical), or null to leave names alone; an identical list is a no-op.
    /// If anything changed: applies inputs and names, plans and evaluates once over all changed keys and names, raises
    /// <see cref="CellsChanged"/> once, and pushes one undo step when <paramref name="record"/> is true.
    /// <see cref="LastRecalc"/> is always set (0 when nothing changed).
    /// </summary>
    private void Commit(IReadOnlyList<CellEdit> edits, IReadOnlyList<KeyValuePair<string, string>>? newNames, bool record)
    {
        var real = new List<CellEdit>(edits.Count);
        foreach (var e in edits)
            if (!string.Equals(e.OldInput, e.NewInput, StringComparison.Ordinal)) real.Add(e);

        IReadOnlyList<KeyValuePair<string, string>>? namesBefore = null;
        if (newNames is not null)
        {
            var current = GetNames();
            if (SameNames(current, newNames)) newNames = null;
            else namesBefore = current;
        }

        if (real.Count == 0 && newNames is null)
        {
            LastRecalc = new RecalcStats(0);
            return;
        }

        var changed = new List<int>(real.Count);
        var keys = new int[real.Count];
        for (int i = 0; i < real.Count; i++)
        {
            keys[i] = real[i].Key;
            Apply(real[i].Key, real[i].NewInput);
            changed.Add(real[i].Key);
        }
        List<string> changedNames = newNames is null ? [] : InstallNames(newNames);

        // The graph is fully updated before planning and is not touched while the plan is produced or evaluated.
        var plan = _planner.Plan(keys, changedNames, _isFormula);
        LastRecalc = new RecalcStats(Evaluate(plan, changed));

        // History/dirty state is final before handlers run (they may read IsDirty/CanUndo, or throw).
        if (record) _history.Push(new EditStep(++_lastStepId, real.ToArray(), namesBefore, newNames));
        Raise(changed);
    }

    private static bool SameNames(IReadOnlyList<KeyValuePair<string, string>> a, IReadOnlyList<KeyValuePair<string, string>> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i].Key, b[i].Key, StringComparison.Ordinal) || !string.Equals(a[i].Value, b[i].Value, StringComparison.Ordinal))
                return false;
        return true;
    }

    /// <summary>
    /// Replaces all names with <paramref name="names"/> (validated) and returns the upper-cased names whose target
    /// changed, appeared or disappeared, for the recalc plan. A removed name keeps its graph node (and dependents)
    /// with no target, so redefining it later reconnects its formulas.
    /// </summary>
    private List<string> InstallNames(IReadOnlyList<KeyValuePair<string, string>> names)
    {
        var old = new Dictionary<string, NameDef>(_names, StringComparer.OrdinalIgnoreCase);
        _names.Clear();
        var changed = new List<string>();
        foreach (var (name, refersTo) in names)
        {
            var d = ParseName(name, refersTo, nameof(names));
            AddName(d);
            if (!old.Remove(name, out var prev) || prev.Target != d.Target) changed.Add(name.ToUpperInvariant());
        }
        foreach (var gone in old.Keys)
        {
            string upper = gone.ToUpperInvariant();
            _graph.SetNameTarget(upper, null);
            changed.Add(upper);
        }
        return changed;
    }

    private string CurrentInput(int key) => _store.TryGet(key, out var c) ? c.Input : "";

    /// <summary>Interprets and stores one input, updating the dependency graph.</summary>
    private void Apply(int key, string input)
    {
        bool hadCell = _store.TryGet(key, out var old);
        var cell = InputInterpreter.Interpret(input, _arity);

        if (cell is { Kind: CellKind.Formula })
        {
            // Start from the previous value so recalc can tell whether the value actually changed.
            if (hadCell) cell.Value = old.Value;
            _graph.SetDependencies(key, cell.Deps);
        }
        else if (hadCell && old.Kind == CellKind.Formula)
            _graph.SetDependencies(key, null);

        if (cell is null) _store.Remove(key);
        else _store.Set(key, cell);
    }

    /// <summary>
    /// Evaluates the plan in order, each formula once; cycle members get <c>#CIRC!</c> (§6.7). Records keys whose
    /// value changed. Returns the number of formula cells evaluated.
    /// </summary>
    private int Evaluate(RecalcPlan plan, List<int>? valueChanged)
    {
        var order = plan.Order;
        var cycles = plan.CycleMembers;
        bool anyCycles = cycles.Count > 0;
        for (int i = 0; i < order.Count; i++)
        {
            int key = order[i];
            _store.TryGet(key, out var cell);
            CellValue v;
            if (anyCycles && cycles.Contains(key)) v = Circ;
            else
            {
                _ctx.FormulaRow = key / 1024;
                _ctx.FormulaCol = key % 1024;
                v = _evaluator.Evaluate(cell.Formula!, _ctx);
            }
            if (!v.Equals(cell.Value))
            {
                cell.Value = v;
                valueChanged?.Add(key);
            }
        }
        return order.Count;
    }

    private void Raise(List<int> changed)
    {
        var handler = CellsChanged;
        if (handler is null || changed.Count == 0) return;
        changed.Sort();
        var addresses = new List<string>(changed.Count);
        for (int i = 0; i < changed.Count; i++)
            if (i == 0 || changed[i] != changed[i - 1]) addresses.Add(CellAddress.FromKey(changed[i]).ToString());
        handler(this, new CellsChangedEventArgs(addresses.ToArray()));
    }

    private bool IsFormula(int key) => _store.TryGet(key, out var c) && c.Kind == CellKind.Formula;

    private static int ParseKey(string address, string paramName) =>
        CellAddress.TryParse(address, out var a) ? a.Key : throw new ArgumentException($"Invalid cell address: '{address}'", paramName);

    private static void ValidateInput(string input, string paramName)
    {
        ArgumentNullException.ThrowIfNull(input, paramName);
        if (input.Length > MaxInputLength)
            throw new ArgumentException($"Input exceeds {MaxInputLength} characters.", paramName);
    }

    /// <summary>The read-only view of the workbook (cells and defined names) that formulas evaluate against.</summary>
    private sealed class EvalContext(CellStore store, Dictionary<string, NameDef> names) : IEvalContext
    {
        public int FormulaRow { get; set; }
        public int FormulaCol { get; set; }

        public CellValue GetCellValue(int row, int col) =>
            store.TryGet(row * 1024 + col, out var c) ? c.Value : CellValue.Blank;

        public bool TryResolveName(string name, out RangeAddress target, out bool isRange)
        {
            if (names.TryGetValue(name, out var d) && d.Target is { } t)
            {
                target = t;
                isRange = t.TopLeft != t.BottomRight;
                return true;
            }
            target = default;
            isRange = false;
            return false;
        }

        public CellValue UnresolvedNameValue(string name) =>
            CellValue.FromError(names.ContainsKey(name) ? ErrorCodes.Ref : ErrorCodes.Name);

        public IEnumerable<(int Row, int Col, CellValue Value)> EnumerateNonBlank(RangeAddress range)
        {
            var r = new RangeAddress(
                new CellAddress(Math.Min(range.TopLeft.Row, range.BottomRight.Row), Math.Min(range.TopLeft.Col, range.BottomRight.Col)),
                new CellAddress(Math.Max(range.TopLeft.Row, range.BottomRight.Row), Math.Max(range.TopLeft.Col, range.BottomRight.Col)));
            return r.Cols == 1 ? SingleColumn(r) : ByKey(r);
        }

        /// <summary>Reads the column's row/cell lists directly: no dictionary lookup per cell (§12 P-4, P-7).</summary>
        private IEnumerable<(int Row, int Col, CellValue Value)> SingleColumn(RangeAddress r)
        {
            int c = r.TopLeft.Col, bottom = r.BottomRight.Row;
            if (store.GetColumn(c) is not { } col) yield break;
            var rows = col.Rows;
            var cells = col.Cells;
            for (int i = col.LowerBound(r.TopLeft.Row); i < rows.Count && rows[i] <= bottom; i++)
            {
                var v = cells[i].Value;
                if (v.Kind != ValueKind.Blank) yield return (rows[i], c, v);
            }
        }

        private IEnumerable<(int Row, int Col, CellValue Value)> ByKey(RangeAddress r)
        {
            foreach (int key in store.KeysInRange(r))
            {
                store.TryGet(key, out var c);
                if (c.Value.Kind != ValueKind.Blank) yield return (key / 1024, key % 1024, c.Value);
            }
        }
    }
}
