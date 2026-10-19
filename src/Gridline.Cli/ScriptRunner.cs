using System.Text;
using Gridline.Core;
using Gridline.Core.Addressing;
using Gridline.IO;

namespace Gridline.Cli;

/// <summary>Executes the line-oriented script language (§10.3).</summary>
internal sealed class ScriptRunner
{
    private Workbook _wb;
    private readonly TextWriter _out;
    private bool _anyErr;

    // Reserved for Phase 2 (BEGIN/END batching).
    private bool _batchOpen = false;
    private readonly List<KeyValuePair<string, string>> _pending = [];

    public ScriptRunner(Workbook wb, TextWriter output) { _wb = wb; _out = output; }

    /// <summary>Runs all lines; returns true when no ERR line was printed.</summary>
    public bool Run(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.EndsWith('\r') ? raw[..^1] : raw;
            // Blank (empty or whitespace-only) lines and comments are ignored (§10.3).
            if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;
            try { Execute(line); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Err(ex.Message); }
        }
        _ = _batchOpen; _ = _pending;
        return !_anyErr;
    }

    private void Err(string message)
    {
        _anyErr = true;
        _out.Write("ERR\t" + CliApp.OneLine(message) + "\n");
    }

    private void Execute(string line)
    {
        int sp = line.IndexOf(' ');
        string cmd = (sp < 0 ? line : line[..sp]).ToUpperInvariant();
        string rest = sp < 0 ? "" : line[(sp + 1)..];
        switch (cmd)
        {
            case "SET":
            case "SETX":
            {
                int sp2 = rest.IndexOf(' ');
                string addrText = sp2 < 0 ? rest : rest[..sp2];
                string input = sp2 < 0 ? "" : rest[(sp2 + 1)..];
                if (!TryAddr(addrText, out var a)) return;
                if (cmd == "SETX") input = Escaping.Unescape(input);
                _wb.SetCell(a, input);
                break;
            }
            case "GET":
                if (TryAddr(FirstToken(rest), out var g))
                    _out.Write(g + "\t" + _wb.GetValue(g).ToCanonicalString() + "\n");
                break;
            case "INPUT":
                if (TryAddr(FirstToken(rest), out var i))
                    _out.Write(i + "\t" + Escaping.Escape(_wb.GetInput(i)) + "\n");
                break;
            case "DUMP":
            {
                var sb = new StringBuilder();
                foreach (var a in _wb.GetUsedAddresses())
                    sb.Append(a).Append('\t').Append(_wb.GetValue(a).ToCanonicalString()).Append('\n');
                _out.Write(sb.ToString());
                break;
            }
            case "STATS":
                _out.Write("EVALUATED\t" + _wb.LastRecalc.EvaluatedFormulaCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
                break;
            case "SAVE":
                if (rest.Length == 0) { Err("SAVE requires a path"); break; }
                GridlineFile.SaveToPath(_wb, rest);
                break;
            case "LOAD":
                if (rest.Length == 0) { Err("LOAD requires a path"); break; }
                _wb = GridlineFile.LoadFromPath(rest);
                break;
            case "UNDO": case "REDO": case "BEGIN": case "END": case "PASTE": case "DIRTY":
            case "INSERTROWS": case "DELETEROWS": case "INSERTCOLS": case "DELETECOLS":
            case "NAME": case "UNNAME": case "NAMES":
                Err("not implemented");
                break;
            default:
                Err("unknown command: " + (sp < 0 ? line : line[..sp]));
                break;
        }
    }

    private static string FirstToken(string s)
    {
        int sp = s.IndexOf(' ');
        return sp < 0 ? s : s[..sp];
    }

    private bool TryAddr(string text, out string canonical)
    {
        if (CellAddress.TryParse(text, out var a)) { canonical = a.ToString(); return true; }
        canonical = "";
        Err("invalid address: " + text);
        return false;
    }
}
