using System.Globalization;
using Gridline.Core;
using Gridline.Core.Addressing;

namespace Gridline.App;

/// <summary>
/// UI-free editing logic behind the grid, name box, formula bar and status bar (§11.4, §11.7). Every edit goes
/// through <see cref="Workbook.SetCell"/> or one <see cref="Workbook.ApplyBatch"/>, so each is one undo step.
/// </summary>
public sealed class SheetController
{
    /// <summary>Ranges up to this many positions are scanned cell by cell; larger ones scan the used cells.</summary>
    public const int PositionalScanLimit = 4096;

    private Workbook? _workbook;
    private List<(CellAddress Address, string Text)>? _used; // row-major; rebuilt lazily after a change

    public SheetController(GridSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Selection = selection;
    }

    public GridSelection Selection { get; }

    public Workbook? Workbook
    {
        get => _workbook;
        set
        {
            if (ReferenceEquals(_workbook, value)) return;
            if (_workbook is not null) _workbook.CellsChanged -= OnCellsChanged;
            _workbook = value;
            if (_workbook is not null) _workbook.CellsChanged += OnCellsChanged;
            _used = null;
            IsEditing = false;
        }
    }

    public bool IsEditing { get; private set; }

    /// <summary>The cell being edited (the active cell when the edit began).</summary>
    public CellAddress EditAddress { get; private set; }

    /// <summary>Starts an edit of the active cell. <paramref name="initial"/> null means "edit the existing input" (F2).</summary>
    /// <returns>The editor's starting text.</returns>
    public string BeginEdit(string? initial)
    {
        EditAddress = Selection.Active;
        IsEditing = true;
        return initial ?? ActiveInput;
    }

    /// <summary>Writes <paramref name="text"/> to <see cref="EditAddress"/> (one step) and moves the selection by the delta.</summary>
    public void CommitEdit(string text, int dRow = 0, int dCol = 0)
    {
        if (!IsEditing) return;
        IsEditing = false;
        _workbook?.SetCell(EditAddress.ToString(), text);
        if (dRow != 0 || dCol != 0) Selection.MoveBy(dRow, dCol, extend: false);
    }

    public void CancelEdit() => IsEditing = false;

    /// <summary>Formula bar commit: sets the active cell's input (one step).</summary>
    public void CommitInput(string text) => _workbook?.SetCell(Selection.Active.ToString(), text);

    /// <summary>Name box navigation: a single cell address ([$]COL[$]ROW, any case, surrounding spaces ignored).</summary>
    public bool TryNavigate(string text)
    {
        if (!CellAddress.TryParse(text.Trim(), out var a)) return false;
        Selection.MoveTo(a, extend: false);
        return true;
    }

    /// <summary>Clears every used cell in the selection with one <see cref="Workbook.ApplyBatch"/>; returns the cell count.</summary>
    public int ClearSelection()
    {
        if (_workbook is not { } wb) return 0;
        var edits = new List<KeyValuePair<string, string>>();
        foreach (var addr in UsedIn(Selection.Range)) edits.Add(new(addr, ""));
        if (edits.Count > 0) wb.ApplyBatch(edits);
        return edits.Count;
    }

    public string ActiveInput => _workbook?.GetInput(Selection.Active.ToString()) ?? "";

    /// <summary>§8.3 canonical value of the active cell, or "" without a workbook.</summary>
    public string ActiveValueText => _workbook?.GetValue(Selection.Active.ToString()).ToCanonicalString() ?? "";

    /// <summary>Sum of the Number values in the selection, G10 invariant; "" without a workbook.</summary>
    public string SelectionSumText
    {
        get
        {
            if (_workbook is not { } wb) return "";
            double sum = 0;
            foreach (var addr in UsedIn(Selection.Range))
            {
                var v = wb.GetValue(addr);
                if (v.Kind == ValueKind.Number) sum += v.Number;
            }
            return sum.ToString("G10", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Canonical addresses of used cells inside <paramref name="range"/>, row-major.</summary>
    private IEnumerable<string> UsedIn(RangeAddress range)
    {
        var wb = _workbook!;
        if ((long)range.Rows * range.Cols <= PositionalScanLimit)
        {
            for (int r = range.TopLeft.Row; r <= range.BottomRight.Row; r++)
            for (int c = range.TopLeft.Col; c <= range.BottomRight.Col; c++)
            {
                string addr = new CellAddress(r, c).ToString();
                if (wb.GetInput(addr).Length != 0) yield return addr;
            }
            yield break;
        }

        _used ??= BuildUsed(wb);
        foreach (var (a, text) in _used)
            if (range.Contains(a)) yield return text;
    }

    private static List<(CellAddress, string)> BuildUsed(Workbook wb)
    {
        var all = wb.GetUsedAddresses();
        var list = new List<(CellAddress, string)>(all.Count);
        foreach (var s in all) list.Add((CellAddress.Parse(s), s));
        return list;
    }

    private void OnCellsChanged(object? sender, CellsChangedEventArgs e) => _used = null;
}
