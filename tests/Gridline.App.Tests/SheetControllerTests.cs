using System.Globalization;
using Gridline.App;
using Gridline.Core;
using Gridline.Core.Addressing;

namespace Gridline.App.Tests;

public class SheetControllerTests
{
    private static (SheetController C, Workbook W) Make()
    {
        var w = new Workbook();
        var c = new SheetController(new GridSelection()) { Workbook = w };
        return (c, w);
    }

    [Fact]
    public void ManualScript_TypeCommitMoveUndo()
    {
        var (c, w) = Make();
        Assert.Equal("5", c.BeginEdit("5"));
        c.CommitEdit("5", 1, 0);
        Assert.False(c.IsEditing);
        Assert.Equal("A2", c.Selection.Active.ToString());
        c.BeginEdit("=");
        c.CommitEdit("=A1*2", 0, 1);
        Assert.Equal("B2", c.Selection.Active.ToString());
        Assert.Equal(10d, w.GetValue("A2").Number);
        Assert.True(w.Undo());
        Assert.Equal("", w.GetInput("A2"));
        Assert.Equal("5", w.GetInput("A1"));
    }

    [Fact]
    public void BeginEdit_Null_UsesExistingInput()
    {
        var (c, w) = Make();
        w.SetCell("A1", "=1+2");
        Assert.Equal("=1+2", c.BeginEdit(null));
        Assert.True(c.IsEditing);
        Assert.Equal("A1", c.EditAddress.ToString());
    }

    [Fact]
    public void Cancel_LeavesCellUnchanged()
    {
        var (c, w) = Make();
        w.SetCell("A1", "x");
        c.BeginEdit("y");
        c.CancelEdit();
        Assert.False(c.IsEditing);
        Assert.Equal("x", w.GetInput("A1"));
        c.CommitEdit("z"); // not editing: ignored
        Assert.Equal("x", w.GetInput("A1"));
    }

    [Fact]
    public void Commit_IsOneUndoStep_AndWritesToEditAddress()
    {
        var (c, w) = Make();
        c.Selection.MoveTo(CellAddress.Parse("C3"), false);
        c.BeginEdit("1");
        c.Selection.MoveTo(CellAddress.Parse("D4"), false); // e.g. a click lands before the focus-loss commit
        c.CommitEdit("7");
        Assert.Equal("7", w.GetInput("C3"));
        Assert.Equal("D4", c.Selection.Active.ToString());
        Assert.True(w.Undo());
        Assert.False(w.CanUndo);
    }

    [Fact]
    public void CommitInput_FormulaBar_SetsActive()
    {
        var (c, w) = Make();
        c.Selection.MoveTo(CellAddress.Parse("B2"), false);
        c.CommitInput("=3*3");
        Assert.Equal(9d, w.GetValue("B2").Number);
        Assert.Equal("=3*3", c.ActiveInput);
        Assert.Equal("N:9", c.ActiveValueText);
    }

    [Theory]
    [InlineData("$b$7", "B7")]
    [InlineData(" zz100000 ", "ZZ100000")]
    [InlineData("c3", "C3")]
    public void Navigate_Valid(string text, string expected)
    {
        var (c, _) = Make();
        Assert.True(c.TryNavigate(text));
        Assert.Equal(expected, c.Selection.Active.ToString());
        Assert.Equal(expected, c.Selection.Range.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("A0")]
    [InlineData("AAA1")]
    [InlineData("A100001")]
    [InlineData("A1:B2")]
    [InlineData("hello")]
    public void Navigate_Invalid_LeavesSelection(string text)
    {
        var (c, _) = Make();
        c.Selection.MoveTo(CellAddress.Parse("D4"), false);
        Assert.False(c.TryNavigate(text));
        Assert.Equal("D4", c.Selection.Active.ToString());
    }

    [Fact]
    public void ClearSelection_OneStep_OnlyInsideRange()
    {
        var (c, w) = Make();
        w.ApplyBatch([new("A1", "1"), new("B2", "x"), new("C3", "3"), new("D4", "keep")]);
        c.Selection.MoveTo(CellAddress.Parse("A1"), false);
        c.Selection.MoveTo(CellAddress.Parse("C3"), true);
        Assert.Equal(3, c.ClearSelection());
        Assert.Equal(["D4"], w.GetUsedAddresses());
        Assert.True(w.Undo());
        Assert.Equal(["A1", "B2", "C3", "D4"], w.GetUsedAddresses());
    }

    [Fact]
    public void ClearSelection_FullSheet_UsesUsedCellsOnly()
    {
        var (c, w) = Make();
        w.ApplyBatch([new("A1", "1"), new("ZZ100000", "2")]);
        c.Selection.MoveTo(CellAddress.Parse("A1"), false);
        c.Selection.MoveTo(CellAddress.Parse("ZZ100000"), true);
        Assert.Equal(2, c.ClearSelection());
        Assert.Empty(w.GetUsedAddresses());
    }

    [Fact]
    public void ClearSelection_EmptyRange_NoStep()
    {
        var (c, w) = Make();
        Assert.Equal(0, c.ClearSelection());
        Assert.False(w.CanUndo);
    }

    [Fact]
    public void Sum_NumbersOnly_G10()
    {
        var (c, w) = Make();
        w.ApplyBatch([new("A1", "1.5"), new("A2", "text"), new("A3", "=A1*2"), new("A4", "TRUE"), new("A5", "=1/0"), new("B9", "100")]);
        c.Selection.MoveTo(CellAddress.Parse("A1"), false);
        c.Selection.MoveTo(CellAddress.Parse("A5"), true);
        Assert.Equal("4.5", c.SelectionSumText);
        c.Selection.MoveTo(CellAddress.Parse("ZZ100000"), true); // huge range: used-address path
        Assert.Equal("104.5", c.SelectionSumText);
    }

    [Fact]
    public void Sum_Empty_IsZero_And_TracksEdits()
    {
        var (c, w) = Make();
        c.Selection.MoveTo(CellAddress.Parse("ZZ100000"), true);
        Assert.Equal("0", c.SelectionSumText);
        w.SetCell("Q500", "0.1");
        w.SetCell("Q501", "0.2");
        Assert.Equal((0.1 + 0.2).ToString("G10", CultureInfo.InvariantCulture), c.SelectionSumText);
        w.Undo();
        Assert.Equal("0.1", c.SelectionSumText);
    }

    [Fact]
    public void Sum_SmallAndLargePaths_Agree()
    {
        var (c, w) = Make();
        var edits = new List<KeyValuePair<string, string>>();
        for (int r = 1; r <= 200; r++) edits.Add(new($"B{r}", r.ToString(CultureInfo.InvariantCulture)));
        w.ApplyBatch(edits);
        c.Selection.MoveTo(CellAddress.Parse("A1"), false);
        c.Selection.MoveTo(CellAddress.Parse("C200"), true); // 600 cells: positional path
        string small = c.SelectionSumText;
        c.Selection.MoveTo(CellAddress.Parse("C100000"), true); // used-address path
        Assert.Equal(small, c.SelectionSumText);
        Assert.Equal("20100", small);
    }

    [Fact]
    public void ActiveValueText_Canonical()
    {
        var (c, w) = Make();
        Assert.Equal("BLANK", c.ActiveValueText);
        w.SetCell("A1", "hi");
        Assert.Equal("T:hi", c.ActiveValueText);
    }

    [Fact]
    public void NoWorkbook_IsSafe()
    {
        var c = new SheetController(new GridSelection());
        Assert.Equal("", c.ActiveInput);
        Assert.Equal("", c.ActiveValueText);
        Assert.Equal("", c.SelectionSumText);
        Assert.Equal(0, c.ClearSelection());
    }

    [Fact]
    public void ReplacingWorkbook_CancelsEditAndRefreshesCache()
    {
        var (c, w) = Make();
        c.Selection.MoveTo(CellAddress.Parse("ZZ100000"), true);
        w.SetCell("A1", "5");
        Assert.Equal("5", c.SelectionSumText);
        c.BeginEdit("1");
        var w2 = new Workbook();
        w2.SetCell("B1", "7");
        c.Workbook = w2;
        Assert.False(c.IsEditing);
        Assert.Equal("7", c.SelectionSumText);
        w.SetCell("A2", "1"); // old workbook no longer observed
        Assert.Equal("7", c.SelectionSumText);
    }
}
