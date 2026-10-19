using System.Diagnostics;
using System.Text;

namespace Gridline.Cli.Tests;

public class ProcessTests
{
    private static string Exe()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "gridline.exe" : "gridline");
        Assert.True(File.Exists(exe), "missing " + exe);
        return exe;
    }

    private static ProcessStartInfo Psi(string args, Encoding? stdinEncoding = null)
    {
        var psi = new ProcessStartInfo(Exe(), args)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (stdinEncoding is not null) psi.StandardInputEncoding = stdinEncoding;
        return psi;
    }

    private static (int code, byte[] stdout, string stderr) Finish(Process p)
    {
        var errTask = p.StandardError.ReadToEndAsync();
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        return (p.ExitCode, ms.ToArray(), errTask.Result);
    }

    [Fact]
    public void Script_ProducesUtf8LfWithoutBom()
    {
        using var p = Process.Start(Psi("script"))!;
        var bytes = new UTF8Encoding(false).GetBytes("SET A1 é\nGET A1\n");
        p.StandardInput.BaseStream.Write(bytes);
        p.StandardInput.Close();
        var (code, o, _) = Finish(p);
        Assert.Equal(0, code);
        Assert.False(o.Length >= 3 && o[0] == 0xEF && o[1] == 0xBB && o[2] == 0xBF);
        Assert.DoesNotContain((byte)'\r', o);
        Assert.Equal("A1\tT:é\n", new UTF8Encoding(false, true).GetString(o));
    }

    /// <summary>Encoding.UTF8 as StandardInputEncoding writes a BOM preamble first; it must not break line 1 (§10.3).</summary>
    [Fact]
    public void Script_StdinWithUtf8Bom_FirstLineWorks()
    {
        using var p = Process.Start(Psi("script", Encoding.UTF8))!;
        p.StandardInput.Write("SET A1 é\nGET A1\n");
        p.StandardInput.Close();
        var (code, o, err) = Finish(p);
        Assert.Equal("A1\tT:é\n", new UTF8Encoding(false, true).GetString(o));
        Assert.Equal(0, code);
        Assert.Equal("", err);
    }
}
