using Gridline.Cli;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.Cli.Tests;

public sealed class CsvCliTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gridline-csvcli-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string P(string n) => Path.Combine(_dir, n);

    private static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var o = new StringWriter { NewLine = "\r\n" };
        var e = new StringWriter { NewLine = "\r\n" };
        int code = CliApp.Run(args, new StringReader(""), o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact] public void ImportThenEvalAll()
    {
        File.WriteAllText(P("in.csv"), "a,2\n=1,TRUE\n");
        Assert.Equal(0, Run("import-csv", P("in.csv"), P("o.gridline")).code);
        var r = Run("eval", P("o.gridline"), "--all");
        Assert.Equal(0, r.code);
        Assert.Equal("A1\tT:a\nB1\tN:2\nA2\tT:=1\nB2\tB:TRUE\n", r.stdout);
    }

    [Fact] public void ImportWithDelimiter()
    {
        File.WriteAllText(P("in.csv"), "a;b\n");
        Assert.Equal(0, Run("import-csv", P("in.csv"), P("o.gridline"), "--delimiter", ";").code);
        Assert.Equal("A1\tT:a\nB1\tT:b\n", Run("eval", P("o.gridline"), "--all").stdout);
    }

    [Fact] public void ExportWritesBom()
    {
        var wb = new Workbook(); wb.SetCell("A1", "=1+1");
        GridlineFile.SaveToPath(wb, P("f.gridline"));
        Assert.Equal(0, Run("export-csv", P("f.gridline"), P("o.csv")).code);
        var bytes = File.ReadAllBytes(P("o.csv"));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("﻿2\r\n", new System.Text.UTF8Encoding(false).GetString(bytes));
    }

    [Theory] [InlineData("ab")] [InlineData("")] [InlineData("\"")] [InlineData("\n")]
    public void BadDelimiter_Exit1(string d) => Assert.Equal(1, Run("import-csv", P("in.csv"), P("o"), "--delimiter", d).code);

    [Fact] public void BadDelimiter_Export_Exit1() => Assert.Equal(1, Run("export-csv", P("f"), P("o"), "--delimiter", "ab").code);

    [Fact] public void MissingCsv_Exit2() => Assert.Equal(2, Run("import-csv", P("nope.csv"), P("o.gridline")).code);

    [Fact] public void UnterminatedQuote_Exit3()
    {
        File.WriteAllText(P("in.csv"), "\"abc");
        Assert.Equal(3, Run("import-csv", P("in.csv"), P("o.gridline")).code);
    }

    [Fact] public void ExportBadGridlineFile_Exit3()
    {
        File.WriteAllText(P("bad.gridline"), "{}");
        Assert.Equal(3, Run("export-csv", P("bad.gridline"), P("o.csv")).code);
    }
}
