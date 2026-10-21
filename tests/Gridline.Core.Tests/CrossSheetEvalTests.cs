namespace Gridline.Core.Tests;

/// <summary>Cross-sheet references, missing sheets and cycles (CR1.2, §6.7).</summary>
public class CrossSheetEvalTests
{
    private static string V(Workbook wb, string a) => wb.GetValue(a).ToCanonicalString();

    [Fact]
    public void CrossSheet_RefAndRange()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("Data!A1", "2"); wb.SetCell("data!A2", "3");
        wb.SetCell("A1", "=Data!A1*10"); wb.SetCell("A2", "=SUM(Data!A1:A2)"); wb.SetCell("A3", "=SUMIF(Data!A1:A2,\">2\",Data!A1:A2)");
        Assert.Equal("N:20", V(wb, "A1")); Assert.Equal("N:5", V(wb, "A2")); Assert.Equal("N:3", V(wb, "A3"));
        wb.SetCell("Data!A1", "4"); Assert.Equal("N:40", V(wb, "Sheet1!A1"));
        Assert.Equal("N:7", V(wb, "A2")); Assert.Equal("N:7", V(wb, "A3"));
    }

    [Fact]
    public void MissingSheet_ResolvesOnAdd_AndUnresolvesOnUndo()
    {
        var wb = new Workbook(); wb.SetCell("A1", "=New!B1+1");
        Assert.Equal("E:#REF!", V(wb, "A1"));
        wb.AddSheet("New"); Assert.Equal("N:1", V(wb, "A1"));
        wb.SetCell("New!B1", "5"); Assert.Equal("N:6", V(wb, "A1"));
        wb.Undo(); wb.Undo(); Assert.Equal("E:#REF!", V(wb, "A1"));
        wb.Redo(); wb.Redo(); Assert.Equal("N:6", V(wb, "A1"));
    }

    /// <summary>Regression (Task 3 interim): a ref to a missing sheet must not read the same address on Sheet1.</summary>
    [Fact]
    public void MissingSheet_IsRef_NotTheSameAddressOnThisSheet()
    {
        var wb = new Workbook();
        wb.SetCell("B1", "5");
        wb.SetCell("A1", "=Nope!B1");
        wb.SetCell("A2", "=SUM(Nope!B1:B2)");
        wb.SetCell("A3", "=Nope!B1:B2");
        wb.SetCell("A5", "=ISERROR(Nope!B1)");
        wb.SetCell("A7", "=SUMIF(Nope!B1:B2,\">0\")");
        Assert.Equal("E:#REF!", V(wb, "A1"));
        Assert.Equal("E:#REF!", V(wb, "A2"));
        Assert.Equal("E:#REF!", V(wb, "A3"));
        Assert.Equal("B:TRUE", V(wb, "A5"));
        Assert.Equal("E:#REF!", V(wb, "A7"));
    }

    [Fact]
    public void UnqualifiedRef_IsFormulasOwnSheet()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("A1", "1");
        wb.SetCell("Data!A1", "10");
        wb.SetCell("Data!B1", "=A1");
        wb.SetCell("Data!B2", "=SUM(A1:A2)");
        Assert.Equal("Sheet1", wb.ActiveSheet);
        Assert.Equal("N:10", V(wb, "Data!B1"));
        Assert.Equal("N:10", V(wb, "Data!B2"));
        wb.SetCell("Data!A2", "5");
        Assert.Equal("N:15", V(wb, "Data!B2"));
        wb.SetCell("A2", "100"); // Sheet1!A2 does not affect Data's formulas
        Assert.Equal("N:15", V(wb, "Data!B2"));
    }

    [Fact]
    public void CrossSheetCycle_IsCirc()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("Sheet1!A1", "=Data!A1");
        wb.SetCell("Data!A1", "=Sheet1!A1");
        Assert.Equal("E:#CIRC!", V(wb, "Sheet1!A1"));
        Assert.Equal("E:#CIRC!", V(wb, "Data!A1"));
        wb.SetCell("Data!A1", "3");
        Assert.Equal("N:3", V(wb, "Sheet1!A1"));
    }

    [Fact]
    public void QualifiedRef_ToOwnSheet_ResolvesCaseInsensitively()
    {
        var wb = new Workbook();
        wb.SetCell("B1", "4");
        wb.SetCell("A1", "=SHEET1!B1*2");
        Assert.Equal("N:8", V(wb, "A1"));
        wb.SetCell("B1", "5");
        Assert.Equal("N:10", V(wb, "A1"));
    }

    [Fact]
    public void CrossSheet_Names_And_Lookup()
    {
        var wb = new Workbook(); wb.AddSheet("My Data");
        wb.ApplyBatch([new("'My Data'!A1", "a"), new("'My Data'!B1", "1"), new("'my data'!A2", "b"), new("'My Data'!B2", "2")]);
        wb.SetCell("A1", "=VLOOKUP(\"b\",'My Data'!A1:B2,2,0)");
        wb.SetCell("A2", "=COUNTIF('My Data'!A1:B2,\">0\")");
        wb.SetCell("A3", "=ROWS('My Data'!A1:B2)");
        Assert.Equal("N:2", V(wb, "A1"));
        Assert.Equal("N:2", V(wb, "A2"));
        Assert.Equal("N:2", V(wb, "A3"));
    }

    [Fact]
    public void SumIf_SumRangeOnAnotherSheet_BothPaths()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.ApplyBatch([new("Data!A1", "1"), new("Data!A2", "5"), new("B1", "10"), new("B2", "20"), new("B3", "40"), new("A1", "999")]);
        wb.SetCell("C1", "=SUMIF(Data!A1:A3,\">2\",B1:B3)");  // non-blank criteria: walks Data, reads Sheet1
        wb.SetCell("C2", "=SUMIF(Data!A1:A3,\"\",B1:B3)");    // blank criteria: walks Sheet1, reads Data
        Assert.Equal("N:20", V(wb, "C1"));
        Assert.Equal("N:40", V(wb, "C2"));
    }

    [Fact]
    public void CellsChanged_QualifiedWhenSeveralSheets()
    {
        var wb = new Workbook();
        var seen = new List<string>();
        wb.CellsChanged += (_, e) => seen.AddRange(e.Addresses);
        wb.SetCell("A1", "1");
        Assert.Equal(["A1"], seen);
        wb.AddSheet("My Data", 0);
        seen.Clear();
        wb.SetCell("'My Data'!B1", "=Sheet1!A1");
        wb.SetCell("A1", "2");
        Assert.Equal(["'My Data'!B1", "'My Data'!B1", "Sheet1!A1"], seen);
    }
}
