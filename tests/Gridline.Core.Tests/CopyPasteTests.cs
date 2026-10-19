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
}
