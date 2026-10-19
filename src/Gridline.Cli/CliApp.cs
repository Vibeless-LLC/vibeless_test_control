using Gridline.Core;
using Gridline.Core.Addressing;
using Gridline.IO;

namespace Gridline.Cli;

/// <summary>Entry point of the gridline command-line tool (§10).</summary>
public static class CliApp
{
    private const int Usage = 1, IoError = 2, FormatError = 3, ScriptErr = 4, BadAddress = 5;

    public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            if (args.Length == 0) return Fail(stderr, Usage, "usage: gridline <eval|set|script> ...");
            return args[0] switch
            {
                "eval" => Eval(args, stdout, stderr),
                "set" => Set(args, stderr),
                "script" => Script(args, stdin, stdout, stderr),
                _ => Fail(stderr, Usage, "unknown command: " + args[0]),
            };
        }
        catch (GridlineFormatException ex) { return Fail(stderr, FormatError, ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Fail(stderr, IoError, ex.Message); }
    }

    internal static string OneLine(string s) => s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

    private static int Fail(TextWriter stderr, int code, string message)
    {
        stderr.Write("error: " + OneLine(message) + "\n");
        return code;
    }

    private static int Eval(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length < 3) return Fail(stderr, Usage, "usage: gridline eval <file> <addr>... | --all");
        string file = args[1];
        bool all = args.Length == 3 && args[2] == "--all";
        var addrs = new List<string>();
        if (!all)
        {
            for (int i = 2; i < args.Length; i++)
            {
                if (!CellAddress.TryParse(args[i], out var a)) return Fail(stderr, BadAddress, "invalid address: " + args[i]);
                addrs.Add(a.ToString());
            }
        }
        var wb = GridlineFile.LoadFromPath(file);
        if (all) addrs.AddRange(wb.GetUsedAddresses());
        var sb = new System.Text.StringBuilder();
        foreach (var a in addrs) sb.Append(a).Append('\t').Append(wb.GetValue(a).ToCanonicalString()).Append('\n');
        stdout.Write(sb.ToString());
        return 0;
    }

    private static int Set(string[] args, TextWriter stderr)
    {
        var pos = new List<string>();
        string? outPath = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--out")
            {
                if (i + 1 >= args.Length || outPath != null) return Fail(stderr, Usage, "usage: --out requires a path");
                outPath = args[++i];
            }
            else pos.Add(args[i]);
        }
        if (pos.Count != 3) return Fail(stderr, Usage, "usage: gridline set <file> <addr> <input> [--out <path>]");
        var wb = GridlineFile.LoadFromPath(pos[0]);
        if (!CellAddress.TryParse(pos[1], out var addr)) return Fail(stderr, BadAddress, "invalid address: " + pos[1]);
        if (pos[2].Length > Workbook.MaxInputLength) return Fail(stderr, Usage, "input exceeds " + Workbook.MaxInputLength + " characters");
        wb.SetCell(addr.ToString(), pos[2]);
        GridlineFile.SaveToPath(wb, outPath ?? pos[0]);
        return 0;
    }

    private static int Script(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        string? file = null;
        if (args.Length == 3 && args[1] == "--file") file = args[2];
        else if (args.Length != 1) return Fail(stderr, Usage, "usage: gridline script [--file <path>]");
        var wb = file == null ? new Workbook() : GridlineFile.LoadFromPath(file);
        var runner = new ScriptRunner(wb, stdout);
        return runner.Run(stdin.ReadToEnd()) ? 0 : ScriptErr;
    }
}
