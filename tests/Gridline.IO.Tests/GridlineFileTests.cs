using System.Text;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.IO.Tests;

public class GridlineFileTests
{
    private static Workbook Sample()
    {
        var wb = new Workbook();
        wb.SetCell("B1", "5");
        wb.SetCell("A1", "=SUM(B1:B3)");
        wb.SetCell("A2", "hello \"world\"");
        return wb;
    }

    [Fact] public void Save_Names_ExactBytes() { var wb = new Workbook(); wb.DefineName("Total", "$A$1:$A$10"); wb.DefineName("Rate", "$B$1"); Assert.Equal("{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {\n    \"Rate\": \"$B$1\",\n    \"Total\": \"$A$1:$A$10\"\n  },\n  \"cells\": []\n}\n", GridlineFile.Serialize(wb)); }
    [Fact] public void Load_InvalidName_Throws() => Assert.Throws<GridlineFormatException>(() => GridlineFile.Load("{\"format\":\"gridline\",\"version\":1,\"names\":{\"A1\":\"$A$1\"},\"cells\":[]}"u8));
    [Fact] public void Load_RelativeRefersTo_Throws() => Assert.Throws<GridlineFormatException>(() => GridlineFile.Load("{\"format\":\"gridline\",\"version\":1,\"names\":{\"R\":\"A1\"},\"cells\":[]}"u8));
    [Fact] public void Names_NotObject_Throws() => Assert.Throws<GridlineFormatException>(() => GridlineFile.Load("{\"format\":\"gridline\",\"version\":1,\"names\":[],\"cells\":[]}"u8));
    [Fact(Skip = "needs Task 3")] public void Names_RefTargetSurvivesRoundTrip() { var wb = new Workbook(); wb.DefineName("R", "$A$2"); ((dynamic)wb).DeleteRows(2, 1); var bytes = GridlineFile.SerializeToUtf8(wb); var back = GridlineFile.Load(bytes); Assert.Equal("#REF!", back.GetNames()[0].Value); Assert.Equal(bytes, GridlineFile.SerializeToUtf8(back)); }
    [Fact] public void Names_OneByOneRangeSurvivesRoundTrip() { var wb = new Workbook(); wb.DefineName("R", "$A$1:$A$1"); var bytes = GridlineFile.SerializeToUtf8(wb); var back = GridlineFile.Load(bytes); Assert.Equal("$A$1:$A$1", back.GetNames()[0].Value); Assert.Equal(bytes, GridlineFile.SerializeToUtf8(back)); }
    [Fact] public void Save_Empty_ExactBytes() => Assert.Equal("{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {},\n  \"cells\": []\n}\n", GridlineFile.Serialize(new Workbook()));

    [Fact]
    public void Save_Cells_ExactBytes()
    {
        Assert.Equal("{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {},\n  \"cells\": [\n    [\"A1\", \"=SUM(B1:B3)\"],\n    [\"B1\", \"5\"],\n    [\"A2\", \"hello \\\"world\\\"\"]\n  ]\n}\n", GridlineFile.Serialize(Sample()));
    }

    [Fact]
    public void Save_EscapesControlCharsExactly()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "a\\b\b\f\n\r\t\u0001é<>&'+😀");
        Assert.Contains("[\"A1\", \"a\\\\b\\b\\f\\n\\r\\t\\u0001é<>&'+😀\"]", GridlineFile.Serialize(wb));
    }

    [Fact]
    public void Save_LoneSurrogate_EscapedLowercase()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "\uD83D");
        var s = GridlineFile.Serialize(wb);
        Assert.Contains("\"\\ud83d\"", s);
        GridlineFile.SerializeToUtf8(wb);
    }

    [Fact]
    public void SerializeToUtf8_NoBom()
    {
        var b = GridlineFile.SerializeToUtf8(Sample());
        Assert.Equal((byte)'{', b[0]);
        Assert.Equal((byte)'\n', b[^1]);
    }

    [Fact]
    public void Load_ThenSave_ByteIdentical()
    {
        var bytes = GridlineFile.SerializeToUtf8(Sample());
        Assert.Equal(bytes, GridlineFile.SerializeToUtf8(GridlineFile.Load(bytes)));
    }

    /// <summary>The §8.1 layout example, verbatim (LF, trailing newline).</summary>
    private const string SpecExample = """
        {
          "format": "gridline",
          "version": 1,
          "names": {
            "Rate": "$B$1",
            "Total": "$A$1:$A$10"
          },
          "cells": [
            ["A1", "=SUM(B1:B3)*Rate"],
            ["B1", "5"],
            ["A2", "hello \"world\""]
          ]
        }

        """;

    private static byte[] SpecExampleBytes() => Encoding.UTF8.GetBytes(SpecExample.ReplaceLineEndings("\n"));

    [Fact]
    public void Load_SpecExample_SaveByteIdentical()
    {
        var bytes = SpecExampleBytes();
        Assert.Equal((byte)'\n', bytes[^1]);
        var wb = GridlineFile.Load(bytes);
        Assert.Equal(bytes, GridlineFile.SerializeToUtf8(wb));
        AssertFreshHistory(wb);
    }

    [Fact]
    public void Load_SpecExample_NamesAndValues()
    {
        var wb = GridlineFile.Load(SpecExampleBytes());
        Assert.Equal(new[] { KeyValuePair.Create("Rate", "$B$1"), KeyValuePair.Create("Total", "$A$1:$A$10") }, wb.GetNames());
        Assert.Equal("N:25", wb.GetValue("A1").ToCanonicalString());
        AssertFreshHistory(wb);
    }

    [Fact]
    public void Load_NonCanonicalNames_SavedCanonicalAndSorted()
    {
        var wb = GridlineFile.Load("{\"format\":\"gridline\",\"version\":1,\"names\":{\"total\":\"$a$10:$a$1\",\"Gone\":\"#REF!\",\"rate\":\"$b$1\"},\"cells\":[]}"u8);
        Assert.Equal(
            "{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {\n    \"Gone\": \"#REF!\",\n    \"rate\": \"$B$1\",\n    \"total\": \"$A$1:$A$10\"\n  },\n  \"cells\": []\n}\n",
            GridlineFile.Serialize(wb));
    }

    [Theory]
    [InlineData("{\"Rate\":\"B1\"}")]
    [InlineData("{\"Rate\":\"$B$1:B2\"}")]
    [InlineData("{\"A1\":\"$B$1\"}")]
    [InlineData("{\"TRUE\":\"$B$1\"}")]
    [InlineData("{\"1x\":\"$B$1\"}")]
    [InlineData("{\"\":\"$B$1\"}")]
    [InlineData("{\"Rate\":\"$B$1\",\"RATE\":\"$C$1\"}")]
    [InlineData("{\"Rate\":\"$B$1\",\"Rate\":\"$C$1\"}")]
    [InlineData("{\"Rate\":\"#NAME?\"}")]
    [InlineData("{\"Rate\":1}")]
    public void Load_InvalidOrDuplicateNames_Throws(string names) =>
        Assert.Throws<GridlineFormatException>(() => GridlineFile.Load(Encoding.UTF8.GetBytes("{\"format\":\"gridline\",\"version\":1,\"names\":" + names + ",\"cells\":[[\"A1\",\"1\"]]}")));

    [Fact]
    public void Load_RecalcsValues()
    {
        var wb = GridlineFile.Load(GridlineFile.SerializeToUtf8(Sample()));
        Assert.Equal("=SUM(B1:B3)", wb.GetInput("A1"));
        Assert.Equal(5.0, wb.GetValue("A1").Number);
        AssertFreshHistory(wb);
    }

    /// <summary>A loaded workbook is clean with empty history (§9.3).</summary>
    private static void AssertFreshHistory(Workbook wb)
    {
        Assert.False(wb.IsDirty);
        Assert.False(wb.CanUndo);
        Assert.False(wb.CanRedo);
    }

    [Fact]
    public void Load_AcceptsAnyWhitespaceOrderEscapes()
    {
        var wb = GridlineFile.Load("{\"cells\":[[\"a1\",\"\\u0031\"]],\"version\":1,\"format\":\"gridline\",\"extra\":0}"u8);
        Assert.Equal("1", wb.GetInput("A1"));
        AssertFreshHistory(wb);
    }

    [Fact]
    public void Load_MissingCellsAndNames_Empty()
    {
        var wb = GridlineFile.Load("{\"format\":\"gridline\",\"version\":1}"u8);
        Assert.Empty(wb.GetUsedAddresses());
        AssertFreshHistory(wb);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"format\":\"x\",\"version\":1,\"cells\":[]}")]
    [InlineData("{\"format\":\"gridline\",\"cells\":[]}")]
    [InlineData("{\"format\":\"gridline\",\"version\":1.5,\"cells\":[]}")]
    [InlineData("{\"format\":\"gridline\",\"version\":\"1\",\"cells\":[]}")]
    [InlineData("{\"format\":\"gridline\",\"version\":0,\"cells\":[]}")]
    [InlineData("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\"]]}")]
    [InlineData("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\",\"5\",\"6\"]]}")]
    [InlineData("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\",5]]}")]
    [InlineData("{\"format\":\"gridline\",\"version\":1,\"cells\":{}}")]
    [InlineData("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A0\",\"5\"]]}")]
    [InlineData("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\",\"5\"],[\"a1\",\"6\"]]}")]
    [InlineData("{\"format\":\"gridline\",\"version\":1,\"names\":[],\"cells\":[]}")]
    public void Load_Invalid_Throws(string json) => Assert.Throws<GridlineFormatException>(() => GridlineFile.Load(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Load_FutureVersion_Message() =>
        Assert.Contains("unsupported version", Assert.Throws<GridlineFormatException>(() => GridlineFile.Load("{\"format\":\"gridline\",\"version\":2,\"cells\":[]}"u8)).Message);

    [Theory]
    [InlineData("99999999999999999999")]
    [InlineData("9223372036854775808")]
    [InlineData("2")]
    public void Load_HugeOrFutureIntegerVersion_Unsupported(string v) =>
        Assert.Contains("unsupported version", Assert.Throws<GridlineFormatException>(() =>
            GridlineFile.Load(Encoding.UTF8.GetBytes("{\"format\":\"gridline\",\"version\":" + v + ",\"cells\":[]}"))).Message);

    [Theory]
    [InlineData("-99999999999999999999")]
    [InlineData("99999999999999999999.0")]
    [InlineData("1E20")]
    public void Load_HugeNegativeOrNonIntegerVersion_NotUnsupported(string v) =>
        Assert.DoesNotContain("unsupported version", Assert.Throws<GridlineFormatException>(() =>
            GridlineFile.Load(Encoding.UTF8.GetBytes("{\"format\":\"gridline\",\"version\":" + v + ",\"cells\":[]}"))).Message);

    [Fact]
    public void Load_OverlongInput_Throws()
    {
        var json = "{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\",\"" + new string('x', 32768) + "\"]]}";
        Assert.Throws<GridlineFormatException>(() => GridlineFile.Load(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void SaveToPath_ReplacesAtomically()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.gridline");
            GridlineFile.SaveToPath(new Workbook(), path);
            var wb = Sample();
            GridlineFile.SaveToPath(wb, path);
            Assert.Equal(GridlineFile.SerializeToUtf8(wb), File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(dir));
            var loaded = GridlineFile.LoadFromPath(path);
            Assert.Equal("5", loaded.GetInput("B1"));
            AssertFreshHistory(loaded);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_InvalidUtf8InString_Throws()
    {
        var head = Encoding.UTF8.GetBytes("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\",\"");
        var tail = Encoding.UTF8.GetBytes("\"]]}");
        var bytes = head.Concat(new byte[] { 0xFF }).Concat(tail).ToArray();
        Assert.Throws<GridlineFormatException>(() => GridlineFile.Load(bytes));
    }

    [Fact]
    public void Load_LoneSurrogateInNamesKey_Throws() =>
        Assert.Throws<GridlineFormatException>(() => GridlineFile.Load("{\"format\":\"gridline\",\"version\":1,\"names\":{\"\\ud83d\":\"$B$1\"},\"cells\":[]}"u8));

    [Fact]
    public void Load_LoneSurrogateInFormat_Throws() =>
        Assert.Throws<GridlineFormatException>(() => GridlineFile.Load("{\"format\":\"\\ud83d\",\"version\":1,\"cells\":[]}"u8));

    [Theory]
    [InlineData("1.0")]
    [InlineData("1e0")]
    public void Load_NonIntegerSpelledVersion_Throws(string v) =>
        Assert.Throws<GridlineFormatException>(() => GridlineFile.Load(Encoding.UTF8.GetBytes("{\"format\":\"gridline\",\"version\":" + v + ",\"cells\":[]}")));

    [Fact]
    public void Load_LeadingBom_Accepted()
    {
        var json = Encoding.UTF8.GetBytes("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\",\"x\"]]}");
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(json).ToArray();
        Assert.Equal("x", GridlineFile.Load(bytes).GetInput("A1"));
    }

    [Fact]
    public void Load_PairAdjacentToLoneSurrogate_DecodesExactly()
    {
        var wb = GridlineFile.Load("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\",\"😀\\ud83d\"]]}"u8);
        Assert.Equal("\uD83D\uDE00\uD83D", wb.GetInput("A1"));
    }

    [Fact]
    public void SaveToPath_EmptyPath_ThrowsBeforeWritingAnything()
    {
        var cwd = Directory.GetCurrentDirectory();
        var before = Directory.GetFiles(cwd, "*.tmp").ToHashSet();
        Assert.Throws<ArgumentException>(() => GridlineFile.SaveToPath(Sample(), ""));
        Assert.Subset(before, Directory.GetFiles(cwd, "*.tmp").ToHashSet());
    }

    [Fact]
    public void LoadFromPath_Missing_Propagates() =>
        Assert.ThrowsAny<IOException>(() => GridlineFile.LoadFromPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
}
