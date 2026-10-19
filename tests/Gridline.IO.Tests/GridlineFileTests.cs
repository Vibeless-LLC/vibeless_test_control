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

    [Fact]
    public void Load_RecalcsValues()
    {
        var wb = GridlineFile.Load(GridlineFile.SerializeToUtf8(Sample()));
        Assert.Equal("=SUM(B1:B3)", wb.GetInput("A1"));
        Assert.Equal(5.0, wb.GetValue("A1").Number);
    }

    [Fact]
    public void Load_AcceptsAnyWhitespaceOrderEscapes()
    {
        var wb = GridlineFile.Load("{\"cells\":[[\"a1\",\"\\u0031\"]],\"version\":1,\"format\":\"gridline\",\"extra\":0}"u8);
        Assert.Equal("1", wb.GetInput("A1"));
    }

    [Fact]
    public void Load_MissingCellsAndNames_Empty()
    {
        var wb = GridlineFile.Load("{\"format\":\"gridline\",\"version\":1}"u8);
        Assert.Empty(wb.GetUsedAddresses());
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
    [InlineData("{\"format\":\"gridline\",\"version\":1,\"names\":{\"Rate\":\"$B$1\"},\"cells\":[]}")]
    public void Load_Invalid_Throws(string json) => Assert.Throws<GridlineFormatException>(() => GridlineFile.Load(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Load_FutureVersion_Message() =>
        Assert.Contains("unsupported version", Assert.Throws<GridlineFormatException>(() => GridlineFile.Load("{\"format\":\"gridline\",\"version\":2,\"cells\":[]}"u8)).Message);

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
            Assert.Equal("5", GridlineFile.LoadFromPath(path).GetInput("B1"));
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
    public void LoadFromPath_Missing_Propagates() =>
        Assert.ThrowsAny<IOException>(() => GridlineFile.LoadFromPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
}
