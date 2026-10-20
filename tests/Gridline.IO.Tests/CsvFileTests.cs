using System.Text;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.IO.Tests;

public class CsvFileTests
{
    [Fact] public void Import_MapsFields() { var wb = CsvFile.Import("a,-5,5%,true,=1+1,'x,\r\n,\"q,\"\"z\"\"\"\n"u8); Assert.Equal("a", wb.GetInput("A1")); Assert.Equal("N:-5", wb.GetValue("B1").ToCanonicalString()); Assert.Equal("N:0.05", wb.GetValue("C1").ToCanonicalString()); Assert.Equal("B:TRUE", wb.GetValue("D1").ToCanonicalString()); Assert.Equal("'=1+1", wb.GetInput("E1")); Assert.Equal("''x", wb.GetInput("F1")); Assert.Equal("", wb.GetInput("G1")); Assert.Equal("q,\"z\"", wb.GetInput("B2")); Assert.False(wb.IsDirty); }
    [Fact] public void Import_Utf16LeBom() { var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("é,1")).ToArray(); Assert.Equal("é", CsvFile.Import(bytes).GetInput("A1")); }
    [Fact] public void Import_Utf16BeBom() { var bytes = new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes("é,1")).ToArray(); var wb = CsvFile.Import(bytes); Assert.Equal("é", wb.GetInput("A1")); Assert.Equal("1", wb.GetInput("B1")); }
    [Fact] public void Import_Utf8Bom_Stripped() { var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("é"u8.ToArray()).ToArray(); Assert.Equal("é", CsvFile.Import(bytes).GetInput("A1")); }
    [Fact] public void Import_Cp1252Fallback() => Assert.Equal("é", CsvFile.Import(new byte[] { 0xE9 }).GetInput("A1"));
    [Fact] public void Import_Unterminated_Throws() => Assert.Throws<GridlineFormatException>(() => CsvFile.Import("\"abc"u8));
    [Fact] public void Import_TooManyFields_Throws() => Assert.Throws<GridlineFormatException>(() => CsvFile.Import(Encoding.UTF8.GetBytes(new string(',', 702))));
    [Fact] public void Import_CustomDelimiter() => Assert.Equal("b", CsvFile.Import("a;b"u8, ';').GetInput("B1"));
    [Fact] public void Import_Empty_IsEmptyWorkbook() => Assert.Empty(CsvFile.Import(ReadOnlySpan<byte>.Empty).GetUsedAddresses());
    [Fact] public void Export_Exact() { var wb = new Workbook(); wb.SetCell("A1", "=1/4"); wb.SetCell("B1", " x"); wb.SetCell("C2", "=1/0"); wb.SetCell("A2", "a,b"); wb.SetCell("B2", "TRUE"); Assert.Equal("﻿0.25,\" x\",\r\n\"a,b\",TRUE,#DIV/0!\r\n", new UTF8Encoding(false).GetString(CsvFile.Export(wb))); }
    [Fact] public void Export_Empty_OnlyBom() => Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, CsvFile.Export(new Workbook()));
    [Fact] public void Export_QuotesEmbeddedQuotesAndNewlines() { var wb = new Workbook(); wb.SetCell("A1", "a\"b\nc"); Assert.EndsWith("\"a\"\"b\nc\"\r\n", Encoding.UTF8.GetString(CsvFile.Export(wb))); }
    [Theory] [InlineData('"')] [InlineData('\r')] [InlineData('\n')]
    public void BadDelimiter_Throws(char d) { Assert.Throws<ArgumentException>(() => CsvFile.Export(new Workbook(), d)); Assert.Throws<ArgumentException>(() => CsvFile.Import("a"u8, d)); }

    [Fact] public void ExportToPath_ThenImportFromPath()
    {
        var dir = Directory.CreateTempSubdirectory("gridline-csv-").FullName;
        try
        {
            var p = Path.Combine(dir, "o.csv");
            var wb = new Workbook(); wb.SetCell("B2", "hi");
            CsvFile.ExportToPath(wb, p);
            Assert.Equal("hi", CsvFile.ImportFromPath(p).GetInput("B2"));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
