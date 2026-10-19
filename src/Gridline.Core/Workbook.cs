using Gridline.Core.Addressing;
using Gridline.Core.Dependencies;
using Gridline.Core.Evaluation;
using Gridline.Core.Functions;

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

    /// <summary>One input assignment; an empty input clears the cell.</summary>
    private readonly record struct Edit(int Key, string Input);

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
            wb.Apply(keys[i], cells[i].Value, inputChanged: null);
            if (wb.IsFormula(keys[i])) formulaKeys.Add(keys[i]);
        }
        var plan = wb._planner.PlanAll(formulaKeys);
        wb.LastRecalc = new RecalcStats(wb.Evaluate(plan, valueChanged: null));
        return wb;
    }

    /// <summary>Sets a cell's input; <c>""</c> clears it. Recalculates its dependents before returning.</summary>
    public void SetCell(string address, string input)
    {
        int key = ParseKey(address, nameof(address));
        ValidateInput(input, nameof(input));
        Commit([new Edit(key, input)]);
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
    /// The single mutation path: applies all edits (already validated), then plans and evaluates once, sets
    /// <see cref="LastRecalc"/> and raises <see cref="CellsChanged"/>. Batch/undo (Phase 2) route through here.
    /// </summary>
    private void Commit(IReadOnlyList<Edit> edits)
    {
        var changed = new List<int>();
        var keys = new int[edits.Count];
        for (int i = 0; i < edits.Count; i++)
        {
            keys[i] = edits[i].Key;
            Apply(edits[i].Key, edits[i].Input, changed);
        }

        // The graph is fully updated before planning and is not touched while the plan is produced or evaluated.
        var plan = _planner.Plan(keys, [], _isFormula);
        LastRecalc = new RecalcStats(Evaluate(plan, changed));
        Raise(changed);
    }

    /// <summary>Interprets and stores one input, updating the dependency graph. Records the key if its input changed.</summary>
    private void Apply(int key, string input, List<int>? inputChanged)
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

        if (inputChanged is not null && !string.Equals(hadCell ? old.Input : "", input, StringComparison.Ordinal))
            inputChanged.Add(key);
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
