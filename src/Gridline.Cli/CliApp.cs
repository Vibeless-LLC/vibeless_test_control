using Gridline.Core;
using Gridline.Core.Addressing;
using Gridline.IO;

namespace Gridline.Cli;

/// <summary>Entry point of the gridline command-line tool (§10).</summary>
public static class CliApp
{
    private const int Usage = 1, IoError = 2, FormatError = 3, ScriptErr = 4, BadAddress = 5;
    private const char Bom = (char)0xFEFF;

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
                "import-csv" => Csv(args, stderr, import: true),
                "export-csv" => Csv(args, stderr, import: false),
                _ => Fail(stderr, Usage, "unknown command: " + args[0]),
            };
        }
        catch (GridlineFormatException ex) { return Fail(stderr, FormatError, ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Fail(stderr, IoError, ex.Message); }
    }

    /// <summary>An unusable path (empty, embedded NUL, unsupported form): reported as an I/O error, exit 2 (§10.1).
    /// The path itself is not echoed: it may hold control characters such as NUL.</summary>
    private sealed class InvalidPathException(Exception inner) : IOException("invalid path: " + inner.Message, inner);

    private static Workbook LoadFile(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { throw new InvalidPathException(ex); }
        return GridlineFile.Load(bytes);
    }

    private static void SaveFile(Workbook wb, string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { throw new InvalidPathException(ex); }
        GridlineFile.SaveToPath(wb, full);
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
            // Syntax is checked before the file is loaded (exit 5); sheet names are resolved after (CR1.7).
            for (int i = 2; i < args.Length; i++)
            {
                if (!QualifiedAddress.TryParseCell(args[i], out _, out _)) return Fail(stderr, BadAddress, "invalid address: " + args[i]);
                addrs.Add(args[i]);
            }
        }
        var wb = LoadFile(file);
        if (all) addrs.AddRange(wb.GetUsedAddresses());
        else
        {
            for (int i = 0; i < addrs.Count; i++)
            {
                if (!TryCanonical(wb, addrs[i], out var c)) return Fail(stderr, BadAddress, "invalid address: " + addrs[i]);
                addrs[i] = c;
            }
        }
        var sb = new System.Text.StringBuilder();
        foreach (var a in addrs) sb.Append(a).Append('\t').Append(wb.GetValue(a).ToCanonicalString()).Append('\n');
        stdout.Write(sb.ToString());
        return 0;
    }

    /// <summary>Canonical echo after load (CR1.7): false for a syntax error or an unknown sheet (both exit 5).</summary>
    private static bool TryCanonical(Workbook wb, string text, out string canonical)
    {
        try { return ScriptArgs.TryCanonical(wb, text, out canonical); }
        catch (ArgumentException) { canonical = ""; return false; }
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
        // Arguments are validated before the file is touched, like eval: invalid address 5, input too long 1.
        if (!QualifiedAddress.TryParseCell(pos[1], out _, out _)) return Fail(stderr, BadAddress, "invalid address: " + pos[1]);
        if (pos[2].Length > Workbook.MaxInputLength) return Fail(stderr, Usage, "input exceeds " + Workbook.MaxInputLength + " characters");
        var wb = LoadFile(pos[0]);
        if (!TryCanonical(wb, pos[1], out var addr)) return Fail(stderr, BadAddress, "invalid address: " + pos[1]);
        wb.SetCell(addr, pos[2]);
        SaveFile(wb, outPath ?? pos[0]);
        return 0;
    }

    private static int Script(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        string? file = null;
        if (args.Length == 3 && args[1] == "--file") file = args[2];
        else if (args.Length != 1) return Fail(stderr, Usage, "usage: gridline script [--file <path>]");
        var wb = file == null ? new Workbook() : LoadFile(file);
        var runner = new ScriptRunner(wb, stdout);
        string text = stdin.ReadToEnd();
        // A UTF-8 BOM on stdin (e.g. .NET's Encoding.UTF8 preamble) is not part of the first command (§10.3).
        if (text.Length > 0 && text[0] == Bom) text = text[1..];
        return runner.Run(text) ? 0 : ScriptErr;
    }

    /// <summary>import-csv / export-csv (§10.2). Arguments are validated before any file is touched.</summary>
    private static int Csv(string[] args, TextWriter stderr, bool import)
    {
        string cmd = import ? "import-csv <csv>" : "export-csv <file>";
        string usage = "usage: gridline " + cmd + " <out> [--delimiter c] [--sheet <name>]";
        var pos = new List<string>();
        char delimiter = ',';
        bool seenDelim = false;
        string? sheet = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--delimiter")
            {
                if (i + 1 >= args.Length || seenDelim) return Fail(stderr, Usage, usage);
                string d = args[++i];
                if (d.Length != 1 || d[0] is '"' or '\r' or '\n') return Fail(stderr, Usage, "invalid delimiter");
                delimiter = d[0];
                seenDelim = true;
            }
            else if (args[i] == "--sheet")
            {
                // The value is the plain sheet name (one argv entry, no CR1.2 quoting); it must be a valid name (CR1.7).
                if (i + 1 >= args.Length || sheet != null) return Fail(stderr, Usage, usage);
                sheet = args[++i];
                if (!SheetNames.IsValid(sheet)) return Fail(stderr, Usage, "invalid sheet name");
            }
            else pos.Add(args[i]);
        }
        if (pos.Count != 2) return Fail(stderr, Usage, usage);
        string full;
        try { full = Path.GetFullPath(pos[1]); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { throw new InvalidPathException(ex); }
        if (import)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(pos[0]); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { throw new InvalidPathException(ex); }
            if (sheet == null) SaveFile(CsvFile.Import(bytes, delimiter), full);
            else if (File.Exists(full))
            {
                // An existing workbook gains a new sheet (CR1.7); a duplicate name is a usage error and nothing is written.
                var wb = LoadFile(full);
                if (wb.SheetNames.Contains(sheet, StringComparer.OrdinalIgnoreCase)) return Fail(stderr, Usage, "duplicate sheet name: " + sheet);
                // A full workbook (255 sheets) throws InvalidOperationException before anything changes: exit 1, file untouched.
                try
                {
                    using var ms = new MemoryStream(bytes, writable: false);
                    CsvFile.ImportInto(wb, sheet, ms, new CsvOptions(delimiter));
                }
                catch (InvalidOperationException ex) { return Fail(stderr, Usage, ex.Message); }
                SaveFile(wb, full);
            }
            else
            {
                var wb = CsvFile.Import(bytes, delimiter);
                // RenameSheet throws InvalidOperationException if a rewritten formula would exceed the input limit.
                try { wb.RenameSheet(wb.ActiveSheet, sheet); }
                catch (InvalidOperationException ex) { return Fail(stderr, Usage, ex.Message); }
                wb.MarkSaved();
                SaveFile(wb, full);
            }
        }
        else
        {
            var wb = LoadFile(pos[0]);
            if (sheet != null && !wb.SheetNames.Contains(sheet, StringComparer.OrdinalIgnoreCase)) return Fail(stderr, Usage, "unknown sheet: " + sheet);
            CsvFile.ExportToPath(wb, full, delimiter, sheet);
        }
        return 0;
    }
}
