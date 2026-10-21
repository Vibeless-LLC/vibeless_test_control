using System.Text;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.IO.Tests;

public class CsvSheetTests
{
    private static MemoryStream S(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact] public void ImportInto_AppendsSheet_OneUndoStep()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "keep");
        CsvFile.ImportInto(wb, "Data", S("1,=x\n"));
        Assert.Equal(new[] { "Sheet1", "Data" }, wb.SheetNames);
        Assert.Equal("1", wb.GetInput("Data!A1"));
        Assert.Equal("'=x", wb.GetInput("Data!B1"));
        Assert.Equal("Sheet1", wb.ActiveSheet);
        wb.Undo();
        Assert.Equal(new[] { "Sheet1" }, wb.SheetNames);
        Assert.Equal("keep", wb.GetInput("A1"));
        wb.Redo();
        Assert.Equal("'=x", wb.GetInput("Data!B1"));
    }

    [Fact] public void ImportInto_ExistingName_Throws()
    {
        var wb = new Workbook();
        Assert.Throws<ArgumentException>(() => CsvFile.ImportInto(wb, "sheet1", S("1")));
        Assert.Throws<ArgumentException>(() => CsvFile.ImportInto(wb, "a:b", S("1")));
    }

    [Fact] public void ImportInto_BadDelimiter_Throws() =>
        Assert.Throws<ArgumentException>(() => CsvFile.ImportInto(new Workbook(), "D", S("1"), new CsvOptions('"')));

    [Fact] public void ImportInto_CustomDelimiter()
    {
        var wb = new Workbook();
        CsvFile.ImportInto(wb, "D", S("a;b"), new CsvOptions(';'));
        Assert.Equal("b", wb.GetInput("D!B1"));
    }

    [Fact] public void ImportInto_Malformed_ChangesNothing()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "x");
        Assert.Throws<GridlineFormatException>(() => CsvFile.ImportInto(wb, "Data", S("\"abc")));
        Assert.Equal(new[] { "Sheet1" }, wb.SheetNames);
        wb.Undo();
        Assert.Equal("", wb.GetInput("A1"));
        Assert.False(wb.CanUndo);
    }

    [Fact] public void AddSheet_WithCells_QualifiedAddress_Throws_ChangesNothing()
    {
        var wb = new Workbook();
        Assert.Throws<ArgumentException>(() => wb.AddSheet("D", null, new[] { new KeyValuePair<string, string>("A1", "1"), new KeyValuePair<string, string>("Sheet1!B1", "2") }));
        Assert.Equal(new[] { "Sheet1" }, wb.SheetNames);
        Assert.False(wb.CanUndo);
    }

    [Fact] public void Export_SheetParameter()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "main");
        CsvFile.ImportInto(wb, "Data", S("1,2\n"));
        Assert.Equal("﻿main\r\n", Encoding.UTF8.GetString(CsvFile.Export(wb)));
        Assert.Equal("﻿1,2\r\n", Encoding.UTF8.GetString(CsvFile.Export(wb, ',', "data")));
        wb.ActiveSheet = "Data";
        Assert.Equal("﻿1,2\r\n", Encoding.UTF8.GetString(CsvFile.Export(wb)));
    }

    [Fact] public void Export_UnknownSheet_Throws()
    {
        var wb = new Workbook();
        Assert.Throws<ArgumentException>(() => CsvFile.Export(wb, ',', "Nope"));
        Assert.Throws<ArgumentException>(() => CsvFile.ExportToPath(wb, Path.Combine(Path.GetTempPath(), "x.csv"), ',', "Nope"));
    }
}
