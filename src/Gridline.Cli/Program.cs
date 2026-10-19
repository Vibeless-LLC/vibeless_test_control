using System.Text;

namespace Gridline.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        var utf8 = new UTF8Encoding(false);
        using var stdin = new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: false);
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { NewLine = "\n", AutoFlush = false };
        using var stderr = new StreamWriter(Console.OpenStandardError(), utf8) { NewLine = "\n", AutoFlush = false };
        int code;
        try { code = CliApp.Run(args, stdin, stdout, stderr); }
        finally { stdout.Flush(); stderr.Flush(); }
        return code;
    }
}
