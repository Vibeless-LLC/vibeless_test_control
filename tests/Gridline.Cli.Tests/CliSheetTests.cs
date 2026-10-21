using Gridline.Cli;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.Cli.Tests;

/// <summary>eval/set with qualified addresses and --sheet on the CSV commands (CR1.7, §10.1, §10.2).</summary>
public sealed class CliSheetTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gridline-clisheet-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string P(string n) => Path.Combine(_dir, n);

    private static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var o = new StringWriter { NewLine = "\r\n" };
        var e = new StringWriter { NewLine = "\r\n" };
        int code = CliApp.Run(args, new StringReader(""), o, e);
        return (code, o.ToString(), e.ToString());
    }

    private string TwoSheetFile(string name = "two.gridline")
    {
        var wb = new Workbook();
        wb.AddSheet("My Data");
        wb.SetCell("'My Data'!A1", "5");
        wb.SetCell("A1", "='My Data'!A1*2");
        var p = P(name);
        GridlineFile.SaveToPath(wb, p);
        return p;
    }

    [Fact]
    public void Cli_Eval_Qualified()
    {
        var f = TwoSheetFile();
        var r = Run("eval", f, "'my data'!a1", "A1", "Sheet1!A1");
        Assert.Equal(0, r.code);
        Assert.Equal("'My Data'!A1\tN:5\nA1\tN:10\nSheet1!A1\tN:10\n", r.stdout);

        var bad = Run("eval", f, "A1", "Nope!A1");
        Assert.Equal(5, bad.code);
        Assert.Equal("", bad.stdout);
        Assert.Contains("Nope", bad.stderr);
    }

    [Fact]
    public void Cli_Eval_BadQualifiedSyntax_Exit5_BeforeLoad() =>
        Assert.Equal(5, Run("eval", P("missing.gridline"), "'My Data!A1").code);

    [Fact]
    public void Cli_Eval_All_Qualified()
    {
        var r = Run("eval", TwoSheetFile(), "--all");
        Assert.Equal(0, r.code);
        Assert.Equal("Sheet1!A1\tN:10\n'My Data'!A1\tN:5\n", r.stdout);
    }

    [Fact]
    public void Cli_Set_Qualified()
    {
        var f = TwoSheetFile();
        Assert.Equal(0, Run("set", f, "'MY DATA'!A1", "7").code);
        Assert.Equal("A1\tN:14\n", Run("eval", f, "A1").stdout);
        Assert.Equal(5, Run("set", f, "Nope!A1", "1").code);
        Assert.Equal(5, Run("set", P("missing.gridline"), "Nope!!A1", "1").code);
    }

    [Fact]
    public void Cli_ImportCsv_Sheet_NewAndExisting()
    {
        File.WriteAllText(P("a.csv"), "1,2\n");
        File.WriteAllText(P("b.csv"), "x\n");
        var o = P("o.gridline");

        Assert.Equal(0, Run("import-csv", P("a.csv"), o, "--sheet", "My Data").code);
        var wb = GridlineFile.LoadFromPath(o);
        Assert.Equal(["My Data"], wb.SheetNames);
        Assert.False(wb.IsDirty);
        Assert.Equal("A1\tN:1\nB1\tN:2\n", Run("eval", o, "--all").stdout);

        Assert.Equal(0, Run("import-csv", P("b.csv"), o, "--sheet", "Other", "--delimiter", ";").code);
        wb = GridlineFile.LoadFromPath(o);
        Assert.Equal(["My Data", "Other"], wb.SheetNames);
        Assert.Equal("My Data", wb.ActiveSheet);
        Assert.Equal("'My Data'!A1\tN:1\n'My Data'!B1\tN:2\nOther!A1\tT:x\n", Run("eval", o, "--all").stdout);

        // Duplicate (case-insensitive) or invalid names: exit 1, file unchanged.
        var before = File.ReadAllBytes(o);
        Assert.Equal(1, Run("import-csv", P("b.csv"), o, "--sheet", "other").code);
        Assert.Equal(1, Run("import-csv", P("b.csv"), o, "--sheet", "bad*").code);
        Assert.Equal(before, File.ReadAllBytes(o));
    }

    [Fact]
    public void Cli_ImportCsv_InvalidSheet_Exit1_BeforeFiles() =>
        Assert.Equal(1, Run("import-csv", P("missing.csv"), P("o.gridline"), "--sheet", "a/b").code);

    [Theory]
    [InlineData("--sheet")]
    [InlineData("--sheet", "A", "--sheet", "B")]
    [InlineData("--sheet", "")]
    public void Cli_Csv_SheetOptionMisuse_Exit1(params string[] extra)
    {
        Assert.Equal(1, Run(["import-csv", P("in.csv"), P("o.gridline"), .. extra]).code);
        Assert.Equal(1, Run(["export-csv", P("f.gridline"), P("o.csv"), .. extra]).code);
    }

    [Fact]
    public void Cli_ExportCsv_Sheet()
    {
        var f = TwoSheetFile();
        Assert.Equal(0, Run("export-csv", f, P("d.csv"), "--sheet", "my data").code);
        Assert.Equal("﻿5\r\n", new System.Text.UTF8Encoding(false).GetString(File.ReadAllBytes(P("d.csv"))));
        Assert.Equal(0, Run("export-csv", f, P("s.csv")).code);
        Assert.Equal("﻿10\r\n", new System.Text.UTF8Encoding(false).GetString(File.ReadAllBytes(P("s.csv"))));
        var r = Run("export-csv", f, P("n.csv"), "--sheet", "Nope");
        Assert.Equal(1, r.code);
        Assert.False(File.Exists(P("n.csv")));
    }
}
