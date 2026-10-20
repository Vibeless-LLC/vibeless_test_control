namespace Gridline.Core.Tests;

public class CopyPasteTests
{
    [Fact] public void Paste_ShiftsRelative() { var wb = new Workbook(); wb.SetCell("A1","=b1+$C$1"); wb.CopyPaste("A1","B3"); Assert.Equal("=C3+$C$1", wb.GetInput("B3")); }
    [Fact] public void Paste_ZeroOffsetStillCanonical() { var wb = new Workbook(); wb.SetCell("A1","= sum( b1 )"); wb.CopyPaste("A1","A1"); Assert.Equal("=SUM(B1)", wb.GetInput("A1")); }
    [Fact] public void Paste_BlankClearsDestination() { var wb = new Workbook(); wb.SetCell("B2","x"); wb.CopyPaste("A1:A2","B1"); Assert.Equal("", wb.GetInput("B2")); }
    [Fact] public void Paste_Overlapping_UsesSnapshot() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A2","=A1*2"); wb.SetCell("A3","x"); wb.CopyPaste("A1:A3","A2"); Assert.Equal("1", wb.GetInput("A2")); Assert.Equal("=A2*2", wb.GetInput("A3")); Assert.Equal("x", wb.GetInput("A4")); }
    [Fact] public void Paste_OffGrid_NoStepNoChange() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.MarkSaved(); Assert.Throws<ArgumentException>(() => wb.CopyPaste("A1:B1","ZZ1")); Assert.False(wb.IsDirty); Assert.True(wb.CanUndo); wb.Undo(); Assert.False(wb.CanUndo); }
    [Fact] public void Paste_RefShiftsOffGrid() { var wb = new Workbook(); wb.SetCell("B2","=A1"); wb.CopyPaste("B2","A1"); Assert.Equal("=#REF!", wb.GetInput("A1")); Assert.Equal("E:#REF!", wb.GetValue("A1").ToCanonicalString()); }
    [Fact] public void Paste_RangeCornerOffGrid_WholeRef() { var wb = new Workbook(); wb.SetCell("B1","=SUM(A1:A2)"); wb.CopyPaste("B1","A1"); Assert.Equal("=SUM(#REF!)", wb.GetInput("A1")); }
    [Fact] public void Paste_ParseErrorVerbatim() { var wb = new Workbook(); wb.SetCell("A1","=1+"); wb.CopyPaste("A1","B1"); Assert.Equal("=1+", wb.GetInput("B1")); }
    [Fact] public void Paste_OneUndoStep() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A2","2"); wb.CopyPaste("A1:A2","B1"); wb.Undo(); Assert.Equal("", wb.GetInput("B1")); Assert.Equal("", wb.GetInput("B2")); }
    [Fact]
    public void Paste_ReemittedFormulaTooLong_ThrowsAndChangesNothing()
    {
        var wb = new Workbook();
        wb.SetCell("B1", "=" + string.Join("+", Enumerable.Repeat("A1", 10_900))); // ~32.7k chars; each A1 grows to ZZ1 when pasted
        Assert.True(wb.GetInput("B1").Length <= Workbook.MaxInputLength);
        wb.SetCell("ZZ2", "keep");
        bool undoBefore = wb.CanUndo;
        int fired = 0;
        wb.CellsChanged += (_, _) => fired++;
        Assert.Throws<ArgumentException>(() => wb.CopyPaste("B1", "ZY1"));
        Assert.Equal("", wb.GetInput("ZY1"));
        Assert.Equal(0, fired);
        Assert.Equal(undoBefore, wb.CanUndo);
        wb.Undo();
        Assert.Equal("", wb.GetInput("ZZ2"));
        Assert.Single(wb.GetUsedAddresses());
    }
    [Fact] public void Paste_InvalidAddresses_Throw()
    {
        var wb = new Workbook();
        Assert.Throws<ArgumentException>(() => wb.CopyPaste("A0", "B1"));
        Assert.Throws<ArgumentException>(() => wb.CopyPaste("A1", "B0"));
        Assert.Throws<ArgumentException>(() => wb.CopyPaste("A1:A100000", "A2"));
        Assert.False(wb.CanUndo);
    }
    [Fact] public void Paste_UsesDefinedNameCasing()
    {
        var wb = Workbook.FromSnapshot([KeyValuePair.Create("A1", "=total+1")], [KeyValuePair.Create("Total", "$B$1")]);
        wb.CopyPaste("A1", "A2");
        Assert.Equal("=Total+1", wb.GetInput("A2"));
    }
    [Fact]
    public void Paste_WholeGridSource_IsSparse()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1"); wb.SetCell("B1", "=a1+1"); wb.SetCell("ZZ100000", "end");
        long before = GC.GetAllocatedBytesForCurrentThread();
        wb.CopyPaste("A1:ZZ100000", "A1");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 64L * 1024 * 1024, $"allocated {allocated}");
        Assert.Equal("=A1+1", wb.GetInput("B1"));
        Assert.Equal("end", wb.GetInput("ZZ100000"));
    }
    [Fact]
    public void Paste_FullColumnSource_ClearsOnlyPopulatedDestination()
    {
        var wb = new Workbook();
        wb.SetCell("A5", "x"); wb.SetCell("B9", "old"); wb.SetCell("B100000", "old2");
        wb.CopyPaste("A1:A100000", "B1");
        Assert.Equal("x", wb.GetInput("B5"));
        Assert.Equal("", wb.GetInput("B9"));
        Assert.Equal("", wb.GetInput("B100000"));
        wb.Undo();
        Assert.Equal("old", wb.GetInput("B9"));
        Assert.Equal("old2", wb.GetInput("B100000"));
    }
}

/// <summary>Timed paste check, serial in the Perf collection so it never overlaps other perf runs (§9.4, §12).</summary>
[Collection(Gridline.Tests.Shared.PerfCollection.Name)]
[Trait("Category", "Perf")]
public class CopyPastePerfTests
{
    [Fact]
    public void Paste_WholeGridSource_Under2s()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1"); wb.SetCell("B1", "=a1+1"); wb.SetCell("ZZ100000", "end");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        wb.CopyPaste("A1:ZZ100000", "A1");
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds} ms");
    }
}
