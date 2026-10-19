using System.Diagnostics;
using System.Text;

namespace Gridline.Cli.Tests;

public class ProcessTests
{
    [Fact]
    public void Script_ProducesUtf8LfWithoutBom()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "gridline.exe" : "gridline");
        Assert.True(File.Exists(exe), "missing " + exe);
        var psi = new ProcessStartInfo(exe, "script")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var bytes = new UTF8Encoding(false).GetBytes("SET A1 é\nGET A1\n");
        p.StandardInput.BaseStream.Write(bytes);
        p.StandardInput.Close();
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        var o = ms.ToArray();
        Assert.Equal(0, p.ExitCode);
        Assert.False(o.Length >= 3 && o[0] == 0xEF && o[1] == 0xBB && o[2] == 0xBF);
        Assert.DoesNotContain((byte)'\r', o);
        Assert.Equal("A1\tT:é\n", new UTF8Encoding(false, true).GetString(o));
    }
}
