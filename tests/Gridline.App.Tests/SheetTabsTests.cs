using Gridline.App;
using Gridline.Core;
using Gridline.Core.Addressing;

namespace Gridline.App.Tests;

/// <summary>UI-free parts of the sheet tab strip and the qualified name box (CR1.8).</summary>
public class SheetTabsTests
{
    private static (SheetController C, Workbook W) Make()
    {
        var w = new Workbook();
        var c = new SheetController(new GridSelection()) { Workbook = w };
        return (c, w);
    }

    [Fact]
    public void NextSheetName_SkipsExisting() =>
        Assert.Equal("Sheet2", SheetTabs.NextSheetName(["Sheet1", "sheet3"]));

    [Fact]
    public void NextSheetName_FillsFirstGapAndIgnoresOtherNames()
    {
        Assert.Equal("Sheet1", SheetTabs.NextSheetName(["Data", "Sheet2"]));
        Assert.Equal("Sheet4", SheetTabs.NextSheetName(["SHEET1", "Sheet2", "sheet3"]));
    }

    [Fact]
    public void MenuEnablement_DeleteNeedsTwoSheets_MovesStopAtEnds()
    {
        Assert.False(SheetTabs.CanDelete(1));
        Assert.True(SheetTabs.CanDelete(2));
        Assert.False(SheetTabs.CanMoveLeft(0));
        Assert.True(SheetTabs.CanMoveLeft(1));
        Assert.False(SheetTabs.CanMoveRight(1, 2));
        Assert.True(SheetTabs.CanMoveRight(0, 2));
    }

    [Fact]
    public void TryRun_ReportsArgumentAndInvalidOperation_NothingChanges()
    {
        var w = new Workbook();
        Assert.False(SheetTabs.TryRun(() => w.DeleteSheet("Sheet1"), out var e1));
        Assert.False(string.IsNullOrEmpty(e1));
        w.AddSheet("Sheet2");
        Assert.False(SheetTabs.TryRun(() => w.RenameSheet("Sheet2", "sheet1"), out var e2));
        Assert.False(string.IsNullOrEmpty(e2));
        Assert.False(SheetTabs.TryRun(() => w.RenameSheet("Sheet2", "a[b"), out _));
        Assert.Equal(["Sheet1", "Sheet2"], w.SheetNames);
        Assert.True(SheetTabs.TryRun(() => w.MoveSheet("Sheet2", 0), out var e3));
        Assert.Null(e3);
        Assert.Equal(["Sheet2", "Sheet1"], w.SheetNames);
    }

    [Fact]
    public void TryNavigate_Qualified_ActivatesSheet()
    {
        var (c, w) = Make();
        w.AddSheet("My Data");
        Assert.True(c.TryNavigate(" 'my data'!$c$5 "));
        Assert.Equal("My Data", w.ActiveSheet);
        Assert.Equal("C5", c.Selection.Active.ToString());
        Assert.True(c.TryNavigate("Sheet1!B2"));
        Assert.Equal("Sheet1", w.ActiveSheet);
        Assert.Equal("B2", c.Selection.Active.ToString());
        Assert.True(c.TryNavigate("d4")); // unqualified stays on the active sheet
        Assert.Equal("Sheet1", w.ActiveSheet);
        Assert.Equal("D4", c.Selection.Active.ToString());
    }

    [Fact]
    public void TryNavigate_UnknownSheet_False()
    {
        var (c, w) = Make();
        w.AddSheet("Sheet2");
        c.Selection.MoveTo(CellAddress.Parse("B7"), extend: false);
        Assert.False(c.TryNavigate("Nope!A1"));
        Assert.False(c.TryNavigate("Sheet2!A0"));
        Assert.False(c.TryNavigate("Sheet2!A1:B2"));
        Assert.False(c.TryNavigate("'Sheet2!A1"));
        Assert.Equal("Sheet1", w.ActiveSheet);
        Assert.Equal("B7", c.Selection.Active.ToString());
    }

    [Fact]
    public void Controller_ReadsAndWritesActiveSheet()
    {
        var (c, w) = Make();
        w.SetCell("A1", "1");
        w.AddSheet("Sheet2");
        w.SetCell("Sheet2!A1", "2");
        Assert.Equal("N:1", c.ActiveValueText);
        w.ActiveSheet = "Sheet2";
        Assert.Equal("N:2", c.ActiveValueText);
        c.CommitInput("5");
        Assert.Equal("5", w.GetInput("Sheet2!A1"));
        Assert.Equal("1", w.GetInput("Sheet1!A1"));
    }

    [Fact]
    public void SelectionSum_LargeRange_MultipleSheets_ActiveSheetOnly()
    {
        var (c, w) = Make();
        w.SetCell("A1", "1");
        w.AddSheet("Sheet2");
        w.SetCell("Sheet2!A2", "10");
        c.Selection.SelectColumn(1, extend: false); // > PositionalScanLimit: filters the used addresses
        Assert.Equal("1", c.SelectionSumText);
        w.ActiveSheet = "Sheet2";
        Assert.Equal("10", c.SelectionSumText);
        Assert.Equal(1, c.ClearSelection());
        Assert.Equal("1", w.GetInput("Sheet1!A1"));
    }

    private sealed class FakeClipboard : IClipboard
    {
        public string? Text;
        public string? GetText() => Text;
        public void SetText(string t) => Text = t;
    }

    [Fact]
    public void Clipboard_MultipleSheets_CopiesActiveSheet_PastesAcrossSheets()
    {
        var w = new Workbook();
        var cb = new FakeClipboard();
        var s = new ClipboardService(cb, () => w, _ => { });
        w.SetCell("A1", "1");
        w.SetCell("B1", "=A1*2");
        w.AddSheet("Sheet2");
        w.SetCell("Sheet2!C3", "x");
        Assert.True(s.Copy(RangeAddress.Parse("A1:B1")).Success);
        Assert.Equal("1\t2", cb.Text);
        w.ActiveSheet = "Sheet2";
        Assert.True(s.Paste(CellAddress.Parse("A5")).Success);
        Assert.Equal("1", w.GetInput("Sheet2!A5"));
        Assert.Equal("=A5*2", w.GetInput("Sheet2!B5"));
        Assert.Equal("", w.GetInput("Sheet1!A5"));
    }

    [Fact]
    public void DocumentSession_DirtyAfterAddSheet_NotAfterActivate()
    {
        var s = new DocumentSession();
        int raised = 0;
        s.StateChanged += (_, _) => raised++;
        s.Workbook.AddSheet("Sheet2");
        Assert.True(s.IsDirty);
        Assert.Equal("Gridline - Untitled *", s.Title);
        Assert.True(raised > 0);

        s.Workbook.Undo();
        Assert.False(s.IsDirty);
        s.Workbook.AddSheet("Sheet2");
        s.Workbook.MarkSaved();
        raised = 0;
        s.Workbook.ActiveSheet = "Sheet2";
        Assert.False(s.IsDirty);
        Assert.Equal("Gridline - Untitled", s.Title);
        Assert.True(raised > 0); // still re-renders
    }

    [Fact]
    public void DocumentSession_SheetsChangedFollowsReplacedWorkbook()
    {
        var s = new DocumentSession();
        var old = s.Workbook;
        s.New();
        int raised = 0;
        s.StateChanged += (_, _) => raised++;
        old.AddSheet("Sheet2");
        Assert.Equal(0, raised);
        s.Workbook.AddSheet("Sheet2");
        Assert.Equal(1, raised);
    }

    [Fact]
    public void DocumentSession_SheetsChanged_OnSheetOpsUndoAndReplace()
    {
        var s = new DocumentSession();
        int raised = 0;
        s.SheetsChanged += (_, _) => raised++;
        s.Workbook.SetCell("A1", "1");
        Assert.Equal(0, raised); // cell edits are not sheet changes
        s.Workbook.AddSheet("Sheet2");
        s.Workbook.ActiveSheet = "Sheet2";
        s.Undo(); // AddSheet undone; Sheet1 active again
        Assert.Equal(3, raised);
        Assert.Equal("Sheet1", s.Workbook.ActiveSheet);
        s.New();
        Assert.Equal(4, raised);
    }
}
