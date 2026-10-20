using System.Runtime.InteropServices;
using Gridline.App;
using Gridline.Core;
using Gridline.Core.Addressing;

namespace Gridline.App.Tests;

public class ClipboardServiceTests
{
    private sealed class FakeClipboard : IClipboard
    {
        public string? Text;
        public int FailGets, FailSets, Gets, Sets;
        public string? GetText() { Gets++; if (FailGets > 0) { FailGets--; throw new COMException("busy"); } return Text; }
        public void SetText(string t) { Sets++; if (FailSets > 0) { FailSets--; throw new COMException("busy"); } Text = t; }
    }

    private static (ClipboardService S, FakeClipboard C, Workbook W) Make()
    {
        var w = new Workbook();
        var c = new FakeClipboard();
        return (new ClipboardService(c, () => w, _ => { }), c, w);
    }

    private static CellAddress A(string s) => CellAddress.Parse(s);
    private static RangeAddress R(string s) => RangeAddress.Parse(s);

    [Fact]
    public void Copy_PutsDisplayedValuesAsTsv()
    {
        var (s, c, w) = Make();
        w.SetCell("A1", "1"); w.SetCell("B1", "=A1+0.5"); w.SetCell("A2", "hi"); w.SetCell("B2", "true");
        Assert.True(s.Copy(R("A1:B2")).Success);
        Assert.Equal("1\t1.5\r\nhi\tTRUE", c.Text);
    }

    [Fact]
    public void Copy_ClipsToUsedExtent()
    {
        var (s, c, w) = Make();
        w.SetCell("B2", "x");
        s.Copy(R("A1:Z50"));
        Assert.Equal("\t\r\n\tx".Replace("\t\r\n\tx", "\t\r\n\tx"), c.Text); // rows 1-2, cols A-B
        Assert.Equal("\t\r\n\tx", c.Text);
    }

    [Fact]
    public void Copy_EmptyIntersection_EmptyText()
    {
        var (s, c, w) = Make();
        w.SetCell("Z9", "x");
        Assert.True(s.Copy(R("A1:B2")).Success);
        Assert.Equal("", c.Text);
    }

    [Fact]
    public void Paste_OwnContent_UsesCopyPasteSemantics()
    {
        var (s, _, w) = Make();
        w.SetCell("A1", "1"); w.SetCell("B1", "=A1*2");
        s.Copy(R("A1:B1"));
        Assert.True(s.Paste(A("A3")).Success);
        Assert.Equal("=A3*2", w.GetInput("B3")); // formula shifted, not the TSV value
    }

    [Fact]
    public void Paste_OwnContent_BlankSourceClearsDestination()
    {
        var (s, _, w) = Make();
        w.SetCell("D4", "old");
        s.Copy(R("A1:B2"));
        Assert.True(s.Paste(A("D4")).Success);
        Assert.Equal("", w.GetInput("D4"));
    }

    [Fact]
    public void Paste_ClipboardChangedByOtherApp_IsExternal()
    {
        var (s, c, w) = Make();
        w.SetCell("A1", "=1+1");
        s.Copy(R("A1"));
        c.Text = "7\t8";
        s.Paste(A("C1"));
        Assert.Equal("7", w.GetInput("C1"));
        Assert.Equal("8", w.GetInput("D1"));
    }

    [Fact]
    public void Paste_External_MappingAndOneUndoStep()
    {
        var (s, c, w) = Make();
        c.Text = "a\t=1+1\r\n'x\t\r\n-5\t\"q\tr\"\r\n";
        Assert.True(s.Paste(A("B2")).Success);
        Assert.Equal("a", w.GetInput("B2"));
        Assert.Equal("'=1+1", w.GetInput("C2"));
        Assert.Equal("''x", w.GetInput("B3"));
        Assert.Equal("", w.GetInput("C3"));
        Assert.Equal(-5d, w.GetValue("B4").Number);
        Assert.Equal("q\tr", w.GetInput("C4"));
        Assert.True(w.Undo());
        Assert.Empty(w.GetUsedAddresses());
    }

    [Fact]
    public void Paste_External_ClippedAtGridEdge()
    {
        var (s, c, w) = Make();
        c.Text = "1\t2\t3\r\n4\t5\t6";
        Assert.True(s.Paste(A("ZY100000")).Success);
        Assert.Equal("1", w.GetInput("ZY100000"));
        Assert.Equal("2", w.GetInput("ZZ100000"));
        Assert.Equal(2, w.GetUsedAddresses().Count);
    }

    [Fact]
    public void Paste_External_MalformedQuote_ReportsAndChangesNothing()
    {
        var (s, c, w) = Make();
        c.Text = "a\t\"oops";
        var r = s.Paste(A("A1"));
        Assert.False(r.Success);
        Assert.NotEqual("", r.Message);
        Assert.False(w.CanUndo);
    }

    [Fact]
    public void Paste_External_TooLongInput_Reports()
    {
        var (s, c, w) = Make();
        c.Text = new string('x', Workbook.MaxInputLength + 1);
        Assert.False(s.Paste(A("A1")).Success);
        Assert.False(w.CanUndo);
    }

    [Fact]
    public void Paste_NoText_Reports()
    {
        var (s, c, w) = Make();
        c.Text = null;
        Assert.False(s.Paste(A("A1")).Success);
    }

    [Fact]
    public void Paste_OwnContent_OffGrid_ReportsNoChange()
    {
        var (s, _, w) = Make();
        w.SetCell("A1", "1"); w.SetCell("B1", "2");
        s.Copy(R("A1:B1"));
        Assert.False(s.Paste(A("ZZ1")).Success);
        Assert.Equal(2, w.GetUsedAddresses().Count);
    }

    [Fact]
    public void BusyClipboard_RetriesThenSucceeds()
    {
        var w = new Workbook(); w.SetCell("A1", "x");
        var c = new FakeClipboard { FailSets = 2, FailGets = 2 };
        var s = new ClipboardService(c, () => w, _ => { });
        Assert.True(s.Copy(R("A1")).Success);
        Assert.Equal(3, c.Sets);
        Assert.True(s.Paste(A("B1")).Success);
        Assert.Equal("x", w.GetInput("B1"));
    }

    [Fact]
    public void BusyClipboard_GivesUpWithMessage_NoCrash()
    {
        var w = new Workbook(); w.SetCell("A1", "x");
        var c = new FakeClipboard { FailSets = 100, FailGets = 100 };
        var s = new ClipboardService(c, () => w, _ => { });
        var r = s.Copy(R("A1"));
        Assert.False(r.Success);
        Assert.False(s.Paste(A("B1")).Success);
        Assert.Equal("", w.GetInput("B1"));
    }

    [Fact]
    public void WorkbookReplaced_OwnStateDropped()
    {
        var w1 = new Workbook(); w1.SetCell("A1", "=1+1");
        var w2 = new Workbook();
        var cur = w1;
        var c = new FakeClipboard();
        var s = new ClipboardService(c, () => cur, _ => { });
        s.Copy(R("A1"));
        cur = w2;
        s.Paste(A("A1")); // same text, different workbook: treated as external text
        Assert.Equal("2", w2.GetInput("A1"));
    }

    [Fact]
    public void WholeGridCopyPaste_Completes()
    {
        var (s, c, w) = Make();
        w.SetCell("A1", "1"); w.SetCell("ZZ100000", "end");
        var r = s.Copy(R("A1:ZZ100000"));
        Assert.True(r.Success);
        Assert.NotEqual("", r.Message); // too large for system text
        Assert.True(s.Paste(A("A1")).Success);
        Assert.Equal("end", w.GetInput("ZZ100000"));
    }
}
