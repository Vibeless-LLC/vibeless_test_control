using Gridline.Core.Addressing;
using Gridline.Core.Commands;
using Gridline.Core.Parsing;

namespace Gridline.Core;

/// <summary>
/// Insert/delete of rows and columns on one sheet (§9.5, CR1.3), and the shared rewrite of formulas that qualify
/// a reference with a given sheet name (used by structure edits, <see cref="RenameSheet"/> and <see cref="DeleteSheet"/>).
/// </summary>
public sealed partial class Workbook
{
    /// <summary>A formula rewrite (§5.5, CR1.2): returns the new tree and whether any reference changed.</summary>
    private delegate Node FormulaRewrite(Node root, out bool changed);

    /// <summary>Inserts rows on the active sheet; see <see cref="InsertRows(string, int, int)"/>.</summary>
    public void InsertRows(int row, int count) => InsertRows(ActiveSheet, row, count);

    /// <summary>Deletes rows on the active sheet; see <see cref="DeleteRows(string, int, int)"/>.</summary>
    public void DeleteRows(int row, int count) => DeleteRows(ActiveSheet, row, count);

    /// <summary>Inserts columns on the active sheet; see <see cref="InsertColumns(string, int, int)"/>.</summary>
    public void InsertColumns(int column, int count) => InsertColumns(ActiveSheet, column, count);

    /// <summary>Deletes columns on the active sheet; see <see cref="DeleteColumns(string, int, int)"/>.</summary>
    public void DeleteColumns(int column, int count) => DeleteColumns(ActiveSheet, column, count);

    /// <summary>
    /// Inserts <paramref name="count"/> empty rows before <paramref name="row"/> on <paramref name="sheet"/>
    /// (case-insensitive name) (§9.5, CR1.3): cells of that sheet at rows ≥ row move down; its own unqualified
    /// references, references qualified with its name from every sheet, and names targeting it are adjusted. One
    /// undo step. Unknown sheet → <see cref="ArgumentException"/>; requires <c>1 ≤ row ≤ MaxRows</c> and
    /// <c>count ≥ 1</c> (<see cref="ArgumentOutOfRangeException"/>); throws <see cref="InvalidOperationException"/>
    /// with no change if a cell with input on that sheet would move past <see cref="MaxRows"/> or a re-emitted
    /// formula would exceed <see cref="MaxInputLength"/>.
    /// </summary>
    public void InsertRows(string sheet, int row, int count)
    {
        var s = SheetByName(sheet, nameof(sheet));
        ValidateInsert(row, count, MaxRows, nameof(row));
        ApplyStructural(s, new StructuralEdit(Axis.Row, row, count, IsInsert: true));
    }

    /// <summary>
    /// Deletes rows <c>[row, row+count-1]</c> on <paramref name="sheet"/> (§9.5, CR1.3): its cells below move up
    /// and references to it are adjusted as for <see cref="InsertRows(string, int, int)"/> (references into the
    /// deleted rows become <c>#REF!</c>). One undo step. Unknown sheet → <see cref="ArgumentException"/>; requires
    /// <c>1 ≤ row</c>, <c>count ≥ 1</c> and <c>row + count - 1 ≤ MaxRows</c> (<see cref="ArgumentOutOfRangeException"/>);
    /// throws <see cref="InvalidOperationException"/> with no change if a re-emitted formula would exceed
    /// <see cref="MaxInputLength"/>.
    /// </summary>
    public void DeleteRows(string sheet, int row, int count)
    {
        var s = SheetByName(sheet, nameof(sheet));
        ValidateDelete(row, count, MaxRows, nameof(row));
        ApplyStructural(s, new StructuralEdit(Axis.Row, row, count, IsInsert: false));
    }

    /// <summary>Inserts columns before 1-based <paramref name="column"/> on a sheet; symmetric to <see cref="InsertRows(string, int, int)"/> with <see cref="MaxColumns"/>.</summary>
    public void InsertColumns(string sheet, int column, int count)
    {
        var s = SheetByName(sheet, nameof(sheet));
        ValidateInsert(column, count, MaxColumns, nameof(column));
        ApplyStructural(s, new StructuralEdit(Axis.Column, column, count, IsInsert: true));
    }

    /// <summary>Deletes columns <c>[column, column+count-1]</c> on a sheet; symmetric to <see cref="DeleteRows(string, int, int)"/> with <see cref="MaxColumns"/>.</summary>
    public void DeleteColumns(string sheet, int column, int count)
    {
        var s = SheetByName(sheet, nameof(sheet));
        ValidateDelete(column, count, MaxColumns, nameof(column));
        ApplyStructural(s, new StructuralEdit(Axis.Column, column, count, IsInsert: false));
    }

    private static void ValidateInsert(int at, int count, int max, string atName)
    {
        if (at < 1 || at > max) throw new ArgumentOutOfRangeException(atName, at, $"Must be between 1 and {max}.");
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "Must be at least 1.");
    }

    private static void ValidateDelete(int at, int count, int max, string atName)
    {
        if (at < 1 || at > max) throw new ArgumentOutOfRangeException(atName, at, $"Must be between 1 and {max}.");
        if (count < 1 || (long)at + count - 1 > max)
            throw new ArgumentOutOfRangeException(nameof(count), count, $"The span must lie within 1..{max}.");
    }

    /// <summary>
    /// The insert/delete core (§9.5) on sheet T (CR1.3). Everything is computed and validated before anything
    /// changes: T's new cell map (moved cells keep their input verbatim unless they are non-parse-error formulas
    /// with a changed reference, which are re-emitted canonically per §5.5; on T, unqualified refs and refs
    /// qualified with T's name apply), the rewrites of formulas on other sheets that qualify a reference with T's
    /// name (only those refs apply), and the adjusted names targeting T (a target that no longer exists becomes
    /// <c>#REF!</c>). Then one <see cref="Commit"/>, one step.
    /// </summary>
    private void ApplyStructural(Sheet sheet, StructuralEdit e)
    {
        bool rows = e.Axis == Axis.Row;
        int max = rows ? MaxRows : MaxColumns;
        var store = sheet.Store;
        int sid = sheet.Id;
        if (e.IsInsert && InsertWouldOverflow(store, e, max))
            throw new InvalidOperationException($"Inserting would move cells with input past the last {(rows ? "row" : "column")}.");

        Func<string?, bool> onSheet = s => s is null || ResolveSheet(s) == sid;
        var moved = new Dictionary<int, string>(store.Count);
        foreach (var (key, cell) in store.All)
        {
            var a = CellAddress.FromKey(key);
            if (!StructuralShift.TryShiftIndex(rows ? a.Row : a.Col, e, max, out int nx)) continue; // deleted
            var na = rows ? new CellAddress(nx, a.Col) : new CellAddress(a.Row, nx);
            string text = cell.Input;
            if (cell.Kind == CellKind.Formula && cell.Formula is { IsParseError: false, Root: { } root })
            {
                var rewritten = ReferenceRewriter.Structural(root, e, onSheet, out bool changed);
                if (changed)
                {
                    text = FormulaPrinter.Print(rewritten, NameCasing, SheetCasing);
                    if (text.Length > MaxInputLength)
                        throw new InvalidOperationException($"A rewritten formula would exceed {MaxInputLength} characters (cell {FormatAddress(sheet, key)}).");
                }
            }
            moved.Add(na.Key, text);
        }

        var edits = new List<CellEdit>(moved.Count * 2);
        foreach (var (key, cell) in store.All)
            if (!moved.ContainsKey(key)) edits.Add(new CellEdit(GlobalKey.Of(sid, key), cell.Input, ""));
        foreach (var (key, text) in moved)
        {
            long g = GlobalKey.Of(sid, key);
            string old = CurrentInput(g);
            if (!string.Equals(old, text, StringComparison.Ordinal)) edits.Add(new CellEdit(g, old, text));
        }

        // Other sheets: only references qualified with T's name move (CR1.3).
        Func<string?, bool> qualifiedT = s => s is not null && ResolveSheet(s) == sid;
        edits.AddRange(RewriteFormulasReferencing(sheet.Name, skipSheet: sid,
            (Node r, out bool c) => ReferenceRewriter.Structural(r, e, qualifiedT, out c), SheetCasing));
        edits.Sort(static (x, y) => x.Key.CompareTo(y.Key)); // sheet id, then row-major: deterministic step contents

        IReadOnlyList<KeyValuePair<string, string>>? names = null;
        if (_names.Count > 0)
        {
            var current = GetNames();
            var list = new KeyValuePair<string, string>[current.Count];
            for (int i = 0; i < current.Count; i++)
            {
                var d = _names[current[i].Key];
                list[i] = new(d.Name, d.SheetId == sid ? ShiftRefersTo(d, sheet.Name, e, max) : current[i].Value);
            }
            names = list;
        }
        Commit(edits, names, newSheets: null, record: true);
    }

    /// <summary>
    /// Edits re-emitting (§5.5) every non-parse-error formula that qualifies a reference with
    /// <paramref name="sheetName"/> (from the graph's sheet-name index, CR1.2) and that <paramref name="rewrite"/>
    /// changes, skipping formulas on <paramref name="skipSheet"/>. Printed with <paramref name="sheetCasing"/>.
    /// Throws <see cref="InvalidOperationException"/> (nothing changed yet) when a result exceeds
    /// <see cref="MaxInputLength"/>.
    /// </summary>
    private List<CellEdit> RewriteFormulasReferencing(string sheetName, int? skipSheet, FormulaRewrite rewrite, Func<string, string?> sheetCasing)
    {
        // The index entry is live: copy it before anything can mutate the graph.
        var formulas = _graph.FormulasReferencingSheet(sheetName.ToUpperInvariant()).ToArray();
        Array.Sort(formulas);
        var edits = new List<CellEdit>();
        foreach (long g in formulas)
        {
            int sid = GlobalKey.Sheet(g);
            if (sid == skipSheet || StoreOf(g) is not { } store || !store.TryGet(GlobalKey.Cell(g), out var cell)) continue;
            if (cell.Kind != CellKind.Formula || cell.Formula is not { IsParseError: false, Root: { } root }) continue;
            var rewritten = rewrite(root, out bool changed);
            if (!changed) continue;
            string text = FormulaPrinter.Print(rewritten, NameCasing, sheetCasing);
            if (text.Length > MaxInputLength)
                throw new InvalidOperationException($"A rewritten formula would exceed {MaxInputLength} characters (cell {FormatAddress(SheetById(sid), GlobalKey.Cell(g))}).");
            edits.Add(new CellEdit(g, cell.Input, text));
        }
        return edits;
    }

    /// <summary>A name's adjusted <c>refersTo</c> (§9.6): same form (cell or range), or <c>#REF!</c> when its target is gone.</summary>
    private static string ShiftRefersTo(NameDef d, string sheetName, StructuralEdit e, int max)
    {
        if (d.Target is not { } t) return ErrorCodes.Ref;
        int r0 = t.TopLeft.Row, r1 = t.BottomRight.Row, c0 = t.TopLeft.Col, c1 = t.BottomRight.Col;
        bool ok = e.Axis == Axis.Row
            ? StructuralShift.TryShiftSpan(r0, r1, e, max, out r0, out r1)
            : StructuralShift.TryShiftSpan(c0, c1, e, max, out c0, out c1);
        return ok ? NameRules.FormatRefersTo(sheetName, new RangeAddress(new CellAddress(r0, c0), new CellAddress(r1, c1)), d.IsRange) : ErrorCodes.Ref;
    }

    /// <summary>True when an insert would push a cell with input on this sheet past the last row/column (checked per column, O(columns)).</summary>
    private static bool InsertWouldOverflow(CellStore store, StructuralEdit e, int max)
    {
        if (e.Axis == Axis.Row)
        {
            for (int c = 1; c <= MaxColumns; c++)
            {
                int last = store.LastRow(c);
                if (last >= e.At && last + (long)e.Count > max) return true;
            }
            return false;
        }
        for (int c = max; c >= e.At && c + (long)e.Count > max; c--)
            if (store.LastRow(c) > 0) return true;
        return false;
    }
}
