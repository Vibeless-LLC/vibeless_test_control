using Gridline.Cli;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.Cli.Tests;

public sealed class CliAppTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gridline-cli-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string P(string name) => Path.Combine(_dir, name);

    private string MakeFile(string name, params (string a, string i)[] cells)
    {
        var wb = new Workbook();
        foreach (var (a, i) in cells) wb.SetCell(a, i);
        var p = P(name);
        GridlineFile.SaveToPath(wb, p);
        return p;
    }

    public static (int code, string stdout, string stderr) Run(string stdin, params string[] args)
    {
        var o = new StringWriter { NewLine = "\r\n" };
        var e = new StringWriter { NewLine = "\r\n" };
        int code = CliApp.Run(args, new StringReader(stdin), o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Eval_AddressesInOrder()
    {
        var f = MakeFile("f.gridline", ("A1", "1"), ("B1", "=A1+1"));
        var r = Run("", "eval", f, "B1", "a1");
        Assert.Equal(0, r.code);
        Assert.Equal("B1\tN:2\nA1\tN:1\n", r.stdout);
    }

    [Fact]
    public void Eval_All()
    {
        var f = MakeFile("f.gridline", ("B2", "x"), ("A1", "1"), ("B1", "=A1+1"));
        var r = Run("", "eval", f, "--all");
        Assert.Equal(0, r.code);
        Assert.Equal("A1\tN:1\nB1\tN:2\nB2\tT:x\n", r.stdout);
    }

    [Fact]
    public void Eval_InvalidAddress_Exit5()
    {
        var f = MakeFile("f.gridline", ("A1", "1"));
        var r = Run("", "eval", f, "A1", "A0");
        Assert.Equal(5, r.code);
        Assert.Equal("", r.stdout);
        Assert.StartsWith("error: ", r.stderr);
        Assert.Single(r.stderr.TrimEnd('\n').Split('\n'));
        Assert.EndsWith("\n", r.stderr);
        Assert.DoesNotContain("\r", r.stderr);
    }

    [Fact]
    public void Eval_MissingFile_Exit2()
    {
        var r = Run("", "eval", P("nope.gridline"), "A1");
        Assert.Equal(2, r.code);
        Assert.Equal("", r.stdout);
    }

    [Fact]
    public void Eval_BadFormat_Exit3()
    {
        var p = P("bad.gridline");
        File.WriteAllText(p, "not json");
        var r = Run("", "eval", p, "A1");
        Assert.Equal(3, r.code);
    }

    [Fact]
    public void Set_InPlaceAndOut()
    {
        var f = MakeFile("f.gridline", ("A1", "1"));
        var g = P("g.gridline");
        Assert.Equal(0, Run("", "set", f, "A1", "=1+1", "--out", g).code);
        Assert.Equal("A1\tN:2\n", Run("", "eval", g, "A1").stdout);
        Assert.Equal("A1\tN:1\n", Run("", "eval", f, "A1").stdout);
        Assert.Equal(0, Run("", "set", f, "a1", "3").code);
        Assert.Equal("A1\tN:3\n", Run("", "eval", f, "A1").stdout);
    }

    [Fact]
    public void Set_InvalidAddress_Exit5_AndTooLong_Exit1()
    {
        var f = MakeFile("f.gridline", ("A1", "1"));
        Assert.Equal(5, Run("", "set", f, "A0", "1").code);
        Assert.Equal(1, Run("", "set", f, "A1", new string('x', Workbook.MaxInputLength + 1)).code);
        Assert.Equal(2, Run("", "set", P("nope"), "A1", "1").code);
    }

    private static void AssertSingleLineError(string stderr)
    {
        Assert.StartsWith("error: ", stderr);
        Assert.EndsWith("\n", stderr);
        Assert.Single(stderr.TrimEnd('\n').Split('\n'));
        Assert.DoesNotContain("\r", stderr);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    public void Eval_InvalidPath_Exit2(string path)
    {
        var r = Run("", "eval", path, "A1");
        Assert.Equal(2, r.code);
        Assert.Equal("", r.stdout);
        AssertSingleLineError(r.stderr);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    public void Set_InvalidOutPath_Exit2_SourceUntouched_NoTempLeft(string outPath)
    {
        var f = MakeFile("f.gridline", ("A1", "1"));
        var before = File.ReadAllBytes(f);
        string cwd = Directory.GetCurrentDirectory();
        var cwdBefore = Directory.GetFiles(cwd, "*.tmp").ToHashSet();

        var r = Run("", "set", f, "A1", "x", "--out", outPath);

        Assert.Equal(2, r.code);
        AssertSingleLineError(r.stderr);
        Assert.Equal(before, File.ReadAllBytes(f));
        Assert.Equal([f], Directory.GetFiles(_dir));
        Assert.Subset(cwdBefore, Directory.GetFiles(cwd, "*.tmp").ToHashSet());
    }

    [Fact]
    public void Set_InvalidSourcePath_Exit2()
    {
        var r = Run("", "set", "", "A1", "x");
        Assert.Equal(2, r.code);
        AssertSingleLineError(r.stderr);
    }

    [Fact]
    public void Set_InvalidAddress_CheckedBeforeLoad_Exit5()
    {
        // §10.1: like eval, an invalid address wins over a missing or malformed file.
        Assert.Equal(5, Run("", "set", P("nope.gridline"), "A0", "1").code);
        var bad = P("bad.gridline");
        File.WriteAllText(bad, "not json");
        Assert.Equal(5, Run("", "set", bad, "A0", "1").code);
    }

    [Fact]
    public void Usage_Exit1()
    {
        var f = MakeFile("f.gridline");
        Assert.Equal(1, Run("").code);
        Assert.Equal(1, Run("", "frob").code);
        Assert.Equal(1, Run("", "eval").code);
        Assert.Equal(1, Run("", "eval", f).code);
        Assert.Equal(1, Run("", "set", f, "A1").code);
        Assert.Equal(1, Run("", "script", "--bogus").code);
    }
}
