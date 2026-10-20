using Gridline.Core;
using Gridline.IO;
using static Gridline.Cli.Tests.CliAppTests;

namespace Gridline.Cli.Tests;

public sealed class ScriptTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gridline-script-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Script_SetGetDumpStats()
    {
        var r = Run("SET A1 2\nSET B1 =A1*3\n# c\n\nGET B1\nDUMP\nSTATS\n", "script");
        Assert.Equal(0, r.code);
        Assert.Equal("B1\tN:6\nA1\tN:2\nB1\tN:6\nEVALUATED\t1\n", r.stdout);
    }

    [Fact]
    public void Script_SetInputIsVerbatimAfterSecondSpace()
    {
        var r = Run("SET A1  x \nINPUT A1\nset a2\nGET A2\n", "script");
        Assert.Equal("A1\t x \nA2\tBLANK\n", r.stdout);
    }

    [Fact]
    public void Script_SetxInput_RoundTripsEscapes()
    {
        var r = Run("SETX A1 a\\tb\\nc\\\\d\\re\nINPUT A1\nGET A1\n", "script");
        Assert.Equal("A1\ta\\tb\\nc\\\\d\\re\nA1\tT:a\\tb\\nc\\\\d\\re\n", r.stdout);
    }

    [Fact]
    public void Script_LeadingBom_Stripped()
    {
        var r = Run((char)0xFEFF + "SET A1 1\nGET A1\n", "script");
        Assert.Equal(0, r.code);
        Assert.Equal("A1\tN:1\n", r.stdout);
    }

    [Fact]
    public void Script_WhitespaceOnlyLines_Ignored()
    {
        var r = Run("SET A1 1\n   \n\t\n \r\nGET A1\n", "script");
        Assert.Equal(0, r.code);
        Assert.Equal("A1\tN:1\n", r.stdout);
    }

    [Fact]
    public void Script_CrLfLines() => Assert.Equal("A1\tN:1\n", Run("SET A1 1\r\nGET A1\r\n", "script").stdout);

    [Fact]
    public void Script_ErrContinues_Exit4()
    {
        var r = Run("GET A0\nFROB\nSET A1 1\nGET A1\n", "script");
        Assert.Equal(4, r.code);
        var lines = r.stdout.Split('\n');
        Assert.StartsWith("ERR\t", lines[0]);
        Assert.StartsWith("ERR\t", lines[1]);
        Assert.Equal("A1\tN:1", lines[2]);
    }

    [Fact]
    public void Script_Phase3CommandsNotImplemented() => Assert.Equal("ERR\tnot implemented\n", Run("INSERTROWS 1 1\n", "script").stdout);

    [Fact]
    public void Script_SaveLoad()
    {
        var p = Path.Combine(_dir, "s.gridline");
        var r = Run("SET A1 5\nSAVE " + p + "\nSET A1 9\nLOAD " + p + "\nGET A1\n", "script");
        Assert.Equal(0, r.code);
        Assert.Equal("A1\tN:5\n", r.stdout);
    }

    [Fact]
    public void Script_LoadMissing_ErrContinues()
    {
        var r = Run("LOAD " + Path.Combine(_dir, "none") + "\nSET A1 1\nGET A1\n", "script");
        Assert.Equal(4, r.code);
        var lines = r.stdout.Split('\n');
        Assert.StartsWith("ERR\t", lines[0]);
        Assert.Equal("A1\tN:1", lines[1]);
    }

    [Fact]
    public void Script_WithFile()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "7");
        var p = Path.Combine(_dir, "w.gridline");
        GridlineFile.SaveToPath(wb, p);
        var r = Run("GET A1\n", "script", "--file", p);
        Assert.Equal(0, r.code);
        Assert.Equal("A1\tN:7\n", r.stdout);
    }

    [Fact]
    public void Script_WithFile_Missing_Exit2() =>
        Assert.Equal(2, Run("", "script", "--file", Path.Combine(_dir, "none")).code);
}
