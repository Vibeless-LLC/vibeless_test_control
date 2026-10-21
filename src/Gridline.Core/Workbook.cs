using Gridline.Core.Addressing;
using Gridline.Core.Commands;
using Gridline.Core.Dependencies;
using Gridline.Core.Evaluation;
using Gridline.Core.Functions;
using Gridline.Core.Parsing;

namespace Gridline.Core;

/// <summary>
/// A workbook of 1–255 ordered sheets (§9.1, CR1.1). Stores cell inputs (§4.3) per sheet, keeps the static
/// dependency graph over global keys (§6.1, CR1.2) and recalculates incrementally and synchronously on every
/// mutation (§6.7, §9.2). Not thread-safe. Sheet members live in <c>Workbook.Sheets.cs</c>, row/column
/// insert and delete in <c>Workbook.Structure.cs</c>.
/// </summary>
public sealed partial class Workbook
{
    public const int MaxRows = 100_000;
    public const int MaxColumns = 702;
    public const int MaxInputLength = 32_767;
    public const int UndoLimit = 100;

    private static readonly CellValue Circ = CellValue.FromError(ErrorCodes.Circ);

    /// <summary>
    /// A defined name (§9.6, CR1.4): its defined casing, the sheet id and target (both null when <c>refersTo</c>
    /// is <c>#REF!</c>), and whether it keeps the range form. Its <c>refersTo</c> text is formatted on demand from
    /// the sheet's current name.
    /// </summary>
    private readonly record struct NameDef(string Name, int? SheetId, RangeAddress? Target, bool IsRange);

    private readonly DependencyGraph _graph = new();
    private readonly RecalcPlanner _planner;
    private readonly FunctionArityAdapter _arity;
    private readonly Evaluator _evaluator;
    private readonly EvalContext _ctx;
    private readonly Func<long, bool> _isFormula;
    private readonly Func<string, int?> _resolveSheet;
    private readonly Dictionary<string, NameDef> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly UndoHistory _history = new(UndoLimit);
    private long _lastStepId;

    public Workbook()
    {
        var registry = FunctionRegistry.CreateDefault();
        _arity = new FunctionArityAdapter(registry);
        _evaluator = new Evaluator(registry.Functions);
        _planner = new RecalcPlanner(_graph);
        _ctx = new EvalContext(this);
        _isFormula = IsFormula;
        _resolveSheet = ResolveSheet;
        InstallInitialSheet(DefaultSheetName);
    }

    /// <summary>Statistics of the most recent mutating call or load (§9.2).</summary>
    public RecalcStats LastRecalc { get; private set; }

    /// <summary>Raised once per mutating call, after recalc, when any input or value changed (§9.2).</summary>
    public event EventHandler<CellsChangedEventArgs>? CellsChanged;

    /// <summary>
    /// Builds a workbook from cell inputs and defined names, then runs a full recalc (§6.7 "Load"). No event is
    /// raised. Throws <see cref="ArgumentException"/> on an invalid or duplicate (case-insensitive) address, an input
    /// over <see cref="MaxInputLength"/>, an invalid or duplicate (case-insensitive) name, or an invalid
    /// <c>refersTo</c> (§9.6: a fully absolute cell or range, or <c>#REF!</c>). The workbook has one sheet,
    /// <c>Sheet1</c>; a <c>refersTo</c> may be unqualified or qualified with that sheet (CR1.4).
    /// </summary>
    public static Workbook FromSnapshot(IReadOnlyList<KeyValuePair<string, string>> cells, IReadOnlyList<KeyValuePair<string, string>> names)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(names);
        var wb = new Workbook();
        var defs = ParseNames(wb, names);
        var keys = new long[cells.Count];
        var seen = new HashSet<long>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            var (address, input) = cells[i];
            long key = wb.ParseCell(address, nameof(cells));
            ValidateInput(input, nameof(cells));
            if (!seen.Add(key)) throw new ArgumentException($"Duplicate cell address: '{address}'", nameof(cells));
            keys[i] = key;
        }
        return wb.Finish(defs, keys, cells);
    }

    /// <summary>
    /// Builds a multi-sheet workbook (CR1.5) and runs a full recalc, raising no event. Sheets keep their order;
    /// <paramref name="activeSheet"/> (case-insensitive) becomes active, and an unqualified <c>refersTo</c> is
    /// qualified with it. Cell addresses are unqualified. Zero or over 255 sheets, an invalid or duplicate sheet
    /// name, an unknown <paramref name="activeSheet"/>, or anything the single-sheet overload rejects →
    /// <see cref="ArgumentException"/>.
    /// </summary>
    public static Workbook FromSnapshot(IReadOnlyList<SheetSnapshot> sheets, IReadOnlyList<KeyValuePair<string, string>> names, string activeSheet)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(activeSheet);
        if (sheets.Count == 0) throw new ArgumentException("A workbook needs at least one sheet.", nameof(sheets));
        if (sheets.Count > Addressing.SheetNames.MaxSheets)
            throw new ArgumentException($"A workbook has at most {Addressing.SheetNames.MaxSheets} sheets.", nameof(sheets));

        var wb = new Workbook();
        wb._sheets.Clear();
        wb._sheetsById.Clear();
        wb._sheetsByName.Clear();
        wb._nextSheetId = 0;
        foreach (var sh in sheets)
        {
            if (sh is null) throw new ArgumentException("Null sheet.", nameof(sheets));
            if (!Addressing.SheetNames.IsValid(sh.Name)) throw new ArgumentException($"Invalid sheet name: '{sh.Name}'", nameof(sheets));
            if (wb._sheetsByName.ContainsKey(sh.Name)) throw new ArgumentException($"Duplicate sheet name: '{sh.Name}'", nameof(sheets));
            ArgumentNullException.ThrowIfNull(sh.Cells);
            wb.InstallInitialSheet(sh.Name);
        }
        wb._activeId = (wb._sheetsByName.TryGetValue(activeSheet, out var act)
            ? act : throw new ArgumentException($"Unknown sheet: '{activeSheet}'", nameof(activeSheet))).Id;

        var defs = ParseNames(wb, names);
        var all = new List<KeyValuePair<string, string>>();
        var keyList = new List<long>();
        var seen = new HashSet<long>();
        foreach (var sh in sheets)
        {
            int id = wb._sheetsByName[sh.Name].Id;
            foreach (var (address, input) in sh.Cells)
            {
                if (!CellAddress.TryParse(address, out var a)) throw new ArgumentException($"Invalid cell address: '{address}'", nameof(sheets));
                ValidateInput(input, nameof(sheets));
                long key = GlobalKey.Of(id, a.Key);
                if (!seen.Add(key)) throw new ArgumentException($"Duplicate cell address: '{address}' on '{sh.Name}'", nameof(sheets));
                keyList.Add(key);
                all.Add(new(address, input));
            }
        }
        return wb.Finish(defs, keyList.ToArray(), all);
    }

    private static NameDef[] ParseNames(Workbook wb, IReadOnlyList<KeyValuePair<string, string>> names)
    {
        var defs = new NameDef[names.Count];
        var seenNames = new HashSet<string>(names.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Count; i++)
        {
            var (name, refersTo) = names[i];
            defs[i] = wb.ParseName(name, refersTo, nameof(names));
            if (!seenNames.Add(name)) throw new ArgumentException($"Duplicate name: '{name}'", nameof(names));
        }
        return defs;
    }

    /// <summary>Installs validated names and cells, then runs the full recalc (§6.7 "Load").</summary>
    private Workbook Finish(NameDef[] defs, long[] keys, IReadOnlyList<KeyValuePair<string, string>> cells)
    {
        foreach (var d in defs) AddName(d);
        var formulaKeys = new HashSet<long>();
        for (int i = 0; i < keys.Length; i++)
        {
            Apply(keys[i], cells[i].Value);
            if (IsFormula(keys[i])) formulaKeys.Add(keys[i]);
        }
        var plan = _planner.PlanAll(formulaKeys);
        LastRecalc = new RecalcStats(Evaluate(plan, valueChanged: null));
        return this;
    }

    /// <summary>
    /// Sets a cell's input; <c>""</c> clears it. Recalculates its dependents before returning. One undo step, unless
    /// the input is unchanged (then no step, no event, not dirty; the cell and its dependents are still re-evaluated).
    /// </summary>
    public void SetCell(string address, string input)
    {
        long key = ParseCell(address, nameof(address));
        ValidateInput(input, nameof(input));
        string old = CurrentInput(key);
        if (string.Equals(old, input, StringComparison.Ordinal))
        {
            // Phase 1 behaviour kept (§9.2): {X} ∪ dependents(X) are re-evaluated; values cannot change.
            var valueChanged = new List<long>();
            LastRecalc = new RecalcStats(Evaluate(_planner.Plan([key], [], [], _isFormula), valueChanged));
            Raise(valueChanged);
            return;
        }
        Commit([new CellEdit(key, old, input)], newNames: null, newSheets: null, record: true);
    }

    /// <summary>
    /// Applies several inputs as one mutation and one undo step (§9.3). Addresses are case-insensitive and the last
    /// edit of an address wins. Every address and input is validated before anything is applied. A batch that
    /// changes no input (including an empty batch) creates no step.
    /// </summary>
    public void ApplyBatch(IReadOnlyList<KeyValuePair<string, string>> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var keys = new long[edits.Count];
        for (int i = 0; i < edits.Count; i++)
        {
            keys[i] = ParseCell(edits[i].Key, nameof(edits));
            ValidateInput(edits[i].Value, nameof(edits));
        }

        // Last wins; each key keeps the position of its first occurrence. OldInput is the pre-commit input.
        var slot = new Dictionary<long, int>(edits.Count);
        var list = new List<CellEdit>(edits.Count);
        for (int i = 0; i < edits.Count; i++)
        {
            var e = new CellEdit(keys[i], CurrentInput(keys[i]), edits[i].Value);
            if (slot.TryGetValue(keys[i], out int j)) list[j] = e;
            else { slot[keys[i]] = list.Count; list.Add(e); }
        }
        Commit(list, newNames: null, newSheets: null, record: true);
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
        if (!QualifiedAddress.TryParse(sourceRange, out var qsrc))
            throw new ArgumentException($"Invalid range: '{sourceRange}'", nameof(sourceRange));
        var srcSheet = SheetOrActive(qsrc.Sheet, nameof(sourceRange));
        var src = qsrc.Range;
        long dstKey = ParseCell(destinationTopLeft, nameof(destinationTopLeft));
        int dstSheet = GlobalKey.Sheet(dstKey);
        var dst = GlobalKey.Address(dstKey);
        int rows = src.Rows, cols = src.Cols;
        if ((long)dst.Row + rows - 1 > MaxRows || (long)dst.Col + cols - 1 > MaxColumns)
            throw new ArgumentException("Destination extends beyond the grid.", nameof(destinationTopLeft));

        int dRow = dst.Row - src.TopLeft.Row, dCol = dst.Col - src.TopLeft.Col;

        // Sparse: cost is O(populated source cells + populated destination cells), never rows * cols.
        var pasted = new Dictionary<int, string>();
        // Each reference keeps its own sheet prefix: unqualified refs follow the destination sheet (CR1.3).
        foreach (var (key, cell) in UsedIn(srcSheet.Store, src))
        {
            string text = cell.Input;
            if (cell.Kind == CellKind.Formula && cell.Formula is { IsParseError: false, Root: { } root })
                text = FormulaPrinter.Print(ReferenceRewriter.Shift(root, dRow, dCol, out _), NameCasing, SheetCasing);
            // Re-emitted formulas can grow (shifted refs get longer, #REF!); keep the input-length invariant.
            if (text.Length > MaxInputLength)
                throw new ArgumentException($"Pasted input exceeds {MaxInputLength} characters.", nameof(destinationTopLeft));
            var a = CellAddress.FromKey(key);
            pasted[new CellAddress(a.Row + dRow, a.Col + dCol).Key] = text;
        }

        var dstRange = new RangeAddress(dst, new CellAddress(dst.Row + rows - 1, dst.Col + cols - 1));
        foreach (var (key, _) in UsedIn(_sheetsById[dstSheet].Store, dstRange)) pasted.TryAdd(key, ""); // source blank clears populated destination

        var keys = new int[pasted.Count];
        pasted.Keys.CopyTo(keys, 0);
        Array.Sort(keys); // row-major
        var edits = new List<CellEdit>(keys.Length);
        foreach (int key in keys)
        {
            long g = GlobalKey.Of(dstSheet, key);
            edits.Add(new CellEdit(g, CurrentInput(g), pasted[key]));
        }
        Commit(edits, newNames: null, newSheets: null, record: true);
    }

    /// <summary>Populated cells inside <paramref name="range"/> via the per-column sorted lists (no per-position probing).</summary>
    private static List<(int Key, Cell Cell)> UsedIn(CellStore store, RangeAddress range)
    {
        var result = new List<(int, Cell)>();
        for (int c = range.TopLeft.Col; c <= range.BottomRight.Col; c++)
        {
            var col = store.GetColumn(c);
            if (col is null) continue;
            for (int i = col.LowerBound(range.TopLeft.Row); i < col.Rows.Count && col.Rows[i] <= range.BottomRight.Row; i++)
                result.Add((new CellAddress(col.Rows[i], c).Key, col.Cells[i]));
        }
        return result;
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

    /// <summary>Restores the exact inputs, names and sheet layout from before the most recent step; false if there is none.</summary>
    public bool Undo()
    {
        if (!_history.TryUndo(out var step)) return false;
        var edits = new CellEdit[step.Cells.Count];
        for (int i = 0; i < edits.Length; i++)
            edits[i] = new CellEdit(step.Cells[i].Key, CurrentInput(step.Cells[i].Key), step.Cells[i].OldInput);
        Commit(edits, step.NamesBefore, step.SheetsBefore, record: false);
        return true;
    }

    /// <summary>Reapplies the most recently undone step; false if there is none.</summary>
    public bool Redo()
    {
        if (!_history.TryRedo(out var step)) return false;
        var edits = new CellEdit[step.Cells.Count];
        for (int i = 0; i < edits.Length; i++)
            edits[i] = new CellEdit(step.Cells[i].Key, CurrentInput(step.Cells[i].Key), step.Cells[i].NewInput);
        Commit(edits, step.NamesAfter, step.SheetsAfter, record: false);
        return true;
    }

    /// <summary>
    /// Defines or redefines a name (§9.6, CR1.4) as one undo step. <paramref name="refersTo"/> must be a fully
    /// absolute cell (<c>$B$1</c>) or range (<c>$A$1:$C$10</c>), optionally sheet-qualified (<c>Data!$B$1</c>); an
    /// unqualified one is qualified with the active sheet and an unknown sheet throws. Its form is kept, so a 1x1
    /// range stays a range. An existing name
    /// (case-insensitive) is replaced and takes the new casing. Identical casing and target is a no-op. Invalid
    /// name or <paramref name="refersTo"/> (including <c>#REF!</c>) throws <see cref="ArgumentException"/>.
    /// </summary>
    public void DefineName(string name, string refersTo)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(refersTo);
        if (refersTo == ErrorCodes.Ref) throw new ArgumentException("refersTo cannot be #REF!", nameof(refersTo));
        var d = ParseName(name, refersTo, nameof(name), nameof(refersTo));
        var list = new List<KeyValuePair<string, string>>(_names.Count + 1);
        foreach (var e in _names.Values)
            if (!e.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase)) list.Add(new(e.Name, RefersToOf(e)));
        list.Add(new(d.Name, RefersToOf(d)));
        ReplaceNames(list);
    }

    /// <summary>Deletes a name (§9.6) as one undo step; dependents become <c>#NAME?</c>. Unknown name throws <see cref="ArgumentException"/>.</summary>
    public void DeleteName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_names.ContainsKey(name)) throw new ArgumentException($"Unknown name: '{name}'", nameof(name));
        var list = new List<KeyValuePair<string, string>>(_names.Count);
        foreach (var e in _names.Values)
            if (!e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) list.Add(new(e.Name, RefersToOf(e)));
        ReplaceNames(list);
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
            canonical[i] = new(d.Name, RefersToOf(d));
        }
        Array.Sort(canonical, static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
        Commit([], canonical, newSheets: null, record: true);
    }

    /// <summary>The exact input of the cell (qualified or active-sheet address, CR1.3), or <c>""</c> if blank.</summary>
    public string GetInput(string address) => CurrentInput(ParseCell(address, nameof(address)));

    /// <summary>The cell's current value (qualified or active-sheet address, CR1.3); Blank for an empty cell.</summary>
    public CellValue GetValue(string address)
    {
        long g = ParseCell(address, nameof(address));
        return StoreOf(g) is { } s && s.TryGet(GlobalKey.Cell(g), out var c) ? c.Value : CellValue.Blank;
    }

    /// <summary>
    /// Defined names as (name in its defined casing, canonical sheet-qualified <c>refersTo</c> such as
    /// <c>Sheet1!$B$1</c>), sorted <see cref="StringComparer.OrdinalIgnoreCase"/> (§9.1, §9.6, CR1.4).
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> GetNames()
    {
        var result = new KeyValuePair<string, string>[_names.Count];
        int i = 0;
        foreach (var d in _names.Values) result[i++] = new(d.Name, RefersToOf(d));
        Array.Sort(result, static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
        return result;
    }

    /// <summary>
    /// Validates one name and its <c>refersTo</c> (§9.6, CR1.4) and returns its definition. An unqualified
    /// <c>refersTo</c> is qualified with the active sheet; a sheet that does not exist throws (names never dangle).
    /// </summary>
    private NameDef ParseName(string name, string refersTo, string paramName, string? refersToParamName = null)
    {
        if (!NameRules.IsValidName(name)) throw new ArgumentException($"Invalid name: '{name}'", paramName);
        if (refersTo == ErrorCodes.Ref) return new NameDef(name, null, null, false);
        if (!NameRules.TryParseRefersTo(refersTo, out string? sheetName, out var target))
            throw new ArgumentException($"Invalid refersTo for name '{name}': '{refersTo}'", refersToParamName ?? paramName);
        Sheet? sheet = Active;
        if (sheetName is not null && !_sheetsByName.TryGetValue(sheetName, out sheet))
            throw new ArgumentException($"Unknown sheet in refersTo for name '{name}': '{refersTo}'", refersToParamName ?? paramName);
        // ':' cannot occur in a sheet name (CR1.1), so it marks the range form.
        bool isRange = refersTo.Contains(':');
        return new NameDef(name, sheet.Id, target, isRange);
    }

    /// <summary>The canonical qualified <c>refersTo</c> of a name, in its sheet's current casing (CR1.4).</summary>
    private string RefersToOf(NameDef d) =>
        d.Target is { } t && d.SheetId is { } id && _sheetsById.TryGetValue(id, out var s)
            ? NameRules.FormatRefersTo(s.Name, t, d.IsRange)
            : ErrorCodes.Ref;

    /// <summary>Stores a (validated) name and points its dependency-graph node at the target (§6.1, §9.6).</summary>
    private void AddName(NameDef d)
    {
        _names[d.Name] = d;
        _graph.SetNameTarget(d.Name.ToUpperInvariant(), d.SheetId, d.Target);
    }

    /// <summary>
    /// The single mutation path (§9.2, §9.3, CR1.1). <paramref name="edits"/> are validated, have distinct keys and
    /// carry the current input as <c>OldInput</c>; no-op edits are dropped. <paramref name="newNames"/> is the
    /// complete sorted name list to install (validated, canonical), or null to leave names alone; an identical list
    /// is a no-op. <paramref name="newSheets"/> is the complete sheet layout to install, or null (likewise).
    /// If anything changed, in order: installs the layout (creating stores for new ids), applies the inputs,
    /// installs the names, drops the stores of removed sheets, rebinds formulas naming a sheet that started or
    /// stopped existing, then plans and evaluates once. Raises <see cref="SheetsChanged"/> and
    /// <see cref="CellsChanged"/> once each when relevant, and pushes one undo step when <paramref name="record"/>
    /// is true. <see cref="LastRecalc"/> is always set (0 when nothing changed).
    /// <paramref name="editsKeepValues"/> (CR1.9, P-8): the edits only re-spell references that point at the same
    /// cells after the commit (a sheet rename), so each edited formula keeps its value and is not a plan root;
    /// formulas that start or stop resolving are still roots through the rebind of step 5.
    /// <paramref name="rewrittenRoots"/> (CR1.9, P-8): for these keys the edit's <c>NewInput</c> is the printed form of
    /// the given AST, which is installed as the cell's formula instead of re-parsing the text.
    /// </summary>
    private void Commit(IReadOnlyList<CellEdit> edits, IReadOnlyList<KeyValuePair<string, string>>? newNames,
        IReadOnlyList<SheetEntry>? newSheets, bool record, bool editsKeepValues = false,
        IReadOnlyDictionary<long, Node>? rewrittenRoots = null)
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

        IReadOnlyList<SheetEntry>? sheetsBefore = null;
        if (newSheets is not null)
        {
            var current = Layout();
            if (current.SequenceEqual(newSheets)) newSheets = null;
            else sheetsBefore = current;
        }

        if (real.Count == 0 && newNames is null && newSheets is null)
        {
            LastRecalc = new RecalcStats(0);
            return;
        }

        // 1. Layout. Removed sheets stay readable (for edits clearing them) until step 4.
        int activeBefore = _activeId;
        List<string> existenceChanged = newSheets is null ? [] : InstallLayout(newSheets);

        // With editsKeepValues, only formulas that name a changed sheet both before and after the edits can change
        // value through the rebind (a rewritten reference keeps pointing at the same cells): snapshot "before".
        Dictionary<string, HashSet<long>>? namedBefore = null;
        if (editsKeepValues)
        {
            namedBefore = new Dictionary<string, HashSet<long>>(existenceChanged.Count, StringComparer.Ordinal);
            foreach (string upper in existenceChanged) namedBefore[upper] = [.. _graph.FormulasReferencingSheet(upper)];
        }

        // 2. Cell edits, grouped per sheet store.
        var changed = new List<long>(real.Count);
        var keys = new long[real.Count];
        var writes = new Dictionary<int, List<(int Key, Cell? Cell)>>();
        for (int i = 0; i < real.Count; i++)
        {
            long g = real[i].Key;
            keys[i] = g;
            int sheet = GlobalKey.Sheet(g);
            if (!writes.TryGetValue(sheet, out var list)) writes[sheet] = list = [];
            // Keys are distinct: reads see the old store.
            var root = rewrittenRoots is not null && rewrittenRoots.TryGetValue(g, out var r) ? r : null;
            list.Add((GlobalKey.Cell(g), Prepare(g, real[i].NewInput, root)));
            changed.Add(g);
        }
        foreach (var (sheet, list) in writes) SheetById(sheet).Store.SetMany(list);

        // 3. Names.
        List<string> changedNames = newNames is null ? [] : InstallNames(newNames);

        // 4. Drop removed sheets: their remaining formulas leave the graph.
        DropRemovedSheets();

        // 5. Rebind formulas naming a sheet whose existence (or id) changed; they are re-evaluated regardless.
        // Edited formulas were registered against the new layout by Prepare, so the rebind skips them.
        var extraRoots = new List<long>();
        if (existenceChanged.Count > 0)
        {
            var edited = new HashSet<long>(keys);
            foreach (string upper in existenceChanged)
                foreach (long f in _graph.Rebind(upper, _resolveSheet, edited))
                    if ((namedBefore is null || namedBefore[upper].Contains(f)) && IsFormula(f)) extraRoots.Add(f);
        }

        // 6. The graph is fully updated before planning and is not touched while the plan is produced or evaluated.
        var plan = _planner.Plan(editsKeepValues ? [] : keys, changedNames, extraRoots, _isFormula);
        LastRecalc = new RecalcStats(Evaluate(plan, changed));

        // History/dirty state is final before handlers run (they may read IsDirty/CanUndo, or throw).
        if (record) _history.Push(new EditStep(++_lastStepId, real.ToArray(), namesBefore, newNames, sheetsBefore, newSheets));
        if (newSheets is not null || _activeId != activeBefore) SheetsChanged?.Invoke(this, EventArgs.Empty);
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
    /// Replaces all names with <paramref name="names"/> (validated) and returns the upper-cased names whose target or form (cell/range)
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
            // A form change alone (1x1 range ↔ cell) changes evaluation too (=R is #VALUE! for a range).
            if (!old.Remove(name, out var prev) || prev.Target != d.Target || prev.SheetId != d.SheetId || prev.IsRange != d.IsRange)
                changed.Add(name.ToUpperInvariant());
        }
        foreach (var gone in old.Keys)
        {
            string upper = gone.ToUpperInvariant();
            _graph.SetNameTarget(upper, null, null);
            changed.Add(upper);
        }
        return changed;
    }

    /// <summary>The cell's input, or <c>""</c> when blank or its sheet does not exist.</summary>
    private string CurrentInput(long key) => StoreOf(key) is { } s && s.TryGet(GlobalKey.Cell(key), out var c) ? c.Input : "";

    /// <summary>Interprets and stores one input, updating the dependency graph.</summary>
    private void Apply(long key, string input)
    {
        var store = SheetById(GlobalKey.Sheet(key)).Store;
        var cell = Prepare(key, input);
        if (cell is null) store.Remove(GlobalKey.Cell(key));
        else store.Set(GlobalKey.Cell(key), cell);
    }

    /// <summary>
    /// Interprets one input and updates the dependency graph for <paramref name="key"/>; the caller stores the
    /// returned cell (null = blank). A formula starts from the key's previous value so recalc can tell whether it
    /// changed. With <paramref name="formulaRoot"/>, <paramref name="input"/> is that AST's printed formula and the
    /// AST is used as its parse (CR1.9, P-8).
    /// </summary>
    private Cell? Prepare(long key, string input, Node? formulaRoot = null)
    {
        bool hadCell = SheetById(GlobalKey.Sheet(key)).Store.TryGet(GlobalKey.Cell(key), out var old);
        var cell = formulaRoot is null ? InputInterpreter.Interpret(input, _arity) : InputInterpreter.FromFormula(input, formulaRoot);

        if (cell is { Kind: CellKind.Formula })
        {
            // Start from the previous value so recalc can tell whether the value actually changed.
            if (hadCell) cell.Value = old.Value;
            _graph.SetDependencies(key, cell.Deps, _resolveSheet);
        }
        else if (hadCell && old.Kind == CellKind.Formula)
            _graph.SetDependencies(key, null, _resolveSheet);
        return cell;
    }

    /// <summary>
    /// Evaluates the plan in order, each formula once; cycle members get <c>#CIRC!</c> (§6.7). Records keys whose
    /// value changed. Returns the number of formula cells evaluated.
    /// </summary>
    private int Evaluate(RecalcPlan plan, List<long>? valueChanged)
    {
        var order = plan.Order;
        var cycles = plan.CycleMembers;
        bool anyCycles = cycles.Count > 0;
        for (int i = 0; i < order.Count; i++)
        {
            long key = order[i];
            int sheet = GlobalKey.Sheet(key);
            _sheetsById[sheet].Store.TryGet(GlobalKey.Cell(key), out var cell);
            CellValue v;
            if (anyCycles && cycles.Contains(key)) v = Circ;
            else
            {
                var at = GlobalKey.Address(key);
                _ctx.FormulaSheet = sheet;
                _ctx.FormulaRow = at.Row;
                _ctx.FormulaCol = at.Col;
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

    /// <summary>
    /// Raises <see cref="CellsChanged"/> with distinct addresses in sheet order, then row-major. Addresses are
    /// qualified iff the workbook has more than one sheet after the commit (CR1.3); cells of sheets that no longer
    /// exist are omitted.
    /// </summary>
    private void Raise(List<long> changed)
    {
        var handler = CellsChanged;
        if (handler is null || changed.Count == 0) return;
        var position = new Dictionary<int, int>(_sheets.Count);
        for (int i = 0; i < _sheets.Count; i++) position[_sheets[i].Id] = i;
        var live = new List<(int Pos, int Cell)>(changed.Count);
        foreach (long g in changed)
            if (position.TryGetValue(GlobalKey.Sheet(g), out int p)) live.Add((p, GlobalKey.Cell(g)));
        if (live.Count == 0) return;
        live.Sort();
        var addresses = new List<string>(live.Count);
        for (int i = 0; i < live.Count; i++)
            if (i == 0 || live[i] != live[i - 1]) addresses.Add(FormatAddress(_sheets[live[i].Pos], live[i].Cell));
        handler(this, new CellsChangedEventArgs(addresses.ToArray()));
    }

    private bool IsFormula(long key) =>
        StoreOf(key) is { } s && s.TryGet(GlobalKey.Cell(key), out var c) && c.Kind == CellKind.Formula;

    private static void ValidateInput(string input, string paramName)
    {
        ArgumentNullException.ThrowIfNull(input, paramName);
        if (input.Length > MaxInputLength)
            throw new ArgumentException($"Input exceeds {MaxInputLength} characters.", paramName);
    }

    /// <summary>The read-only view of the workbook (sheets, cells and defined names) that formulas evaluate against.</summary>
    private sealed class EvalContext(Workbook wb) : IEvalContext
    {
        public int FormulaSheet { get; set; }
        public int FormulaRow { get; set; }
        public int FormulaCol { get; set; }

        public CellValue GetCellValue(int sheet, int row, int col) =>
            wb._sheetsById.TryGetValue(sheet, out var s) && s.Store.TryGet(CellAddress.KeyOf(row, col), out var c) ? c.Value : CellValue.Blank;

        public bool TryResolveSheet(string name, out int sheet)
        {
            if (wb._sheetsByName.TryGetValue(name, out var s)) { sheet = s.Id; return true; }
            sheet = 0;
            return false;
        }

        public bool TryResolveName(string name, out int sheet, out RangeAddress target, out bool isRange)
        {
            if (wb._names.TryGetValue(name, out var d) && d.Target is { } t && d.SheetId is { } id && wb._sheetsById.ContainsKey(id))
            {
                sheet = id;
                target = t;
                isRange = d.IsRange;
                return true;
            }
            sheet = 0;
            target = default;
            isRange = false;
            return false;
        }

        public CellValue UnresolvedNameValue(string name) =>
            CellValue.FromError(wb._names.ContainsKey(name) ? ErrorCodes.Ref : ErrorCodes.Name);

        public IEnumerable<(int Row, int Col, CellValue Value)> EnumerateNonBlank(int sheet, RangeAddress range)
        {
            if (!wb._sheetsById.TryGetValue(sheet, out var s)) return [];
            var r = new RangeAddress(
                new CellAddress(Math.Min(range.TopLeft.Row, range.BottomRight.Row), Math.Min(range.TopLeft.Col, range.BottomRight.Col)),
                new CellAddress(Math.Max(range.TopLeft.Row, range.BottomRight.Row), Math.Max(range.TopLeft.Col, range.BottomRight.Col)));
            return r.Cols == 1 ? SingleColumn(s.Store, r) : ByKey(s.Store, r);
        }

        /// <summary>Reads the column's row/cell lists directly: no dictionary lookup per cell (§12 P-4, P-7).</summary>
        private static IEnumerable<(int Row, int Col, CellValue Value)> SingleColumn(CellStore store, RangeAddress r)
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

        private static IEnumerable<(int Row, int Col, CellValue Value)> ByKey(CellStore store, RangeAddress r)
        {
            foreach (int key in store.KeysInRange(r))
            {
                store.TryGet(key, out var c);
                if (c.Value.Kind == ValueKind.Blank) continue;
                var a = CellAddress.FromKey(key);
                yield return (a.Row, a.Col, c.Value);
            }
        }
    }
}
