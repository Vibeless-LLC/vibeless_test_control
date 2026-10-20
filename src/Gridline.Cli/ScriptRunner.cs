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

    // BEGIN/END batching (§10.3): SET/SETX lines are buffered while a batch is open.
    private bool _batchOpen;
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
        if (_batchOpen) { _batchOpen = false; _pending.Clear(); Err("unterminated BEGIN"); }
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
        if (_batchOpen && cmd is not ("SET" or "SETX" or "END" or "BEGIN"))
        {
            Err("command not allowed inside BEGIN: " + cmd);
            return;
        }
        switch (cmd)
        {
            case "BEGIN":
                if (_batchOpen) Err("BEGIN while a batch is open");
                else _batchOpen = true;
                break;
            case "END":
            {
                if (!_batchOpen) { Err("END without BEGIN"); break; }
                var edits = _pending.ToArray();
                _batchOpen = false;
                _pending.Clear();
                _wb.ApplyBatch(edits);
                break;
            }
            case "UNDO":
                _out.Write(_wb.Undo() ? "OK\n" : "NOOP\n");
                break;
            case "REDO":
                _out.Write(_wb.Redo() ? "OK\n" : "NOOP\n");
                break;
            case "DIRTY":
                _out.Write(_wb.IsDirty ? "TRUE\n" : "FALSE\n");
                break;
            case "PASTE":
            {
                var parts = rest.Split(' ');
                if (parts.Length != 2) { Err("PASTE requires <srcRange> <dstAddr>"); break; }
                _wb.CopyPaste(parts[0], parts[1]);
                break;
            }
            case "SET":
            case "SETX":
            {
                int sp2 = rest.IndexOf(' ');
                string addrText = sp2 < 0 ? rest : rest[..sp2];
                string input = sp2 < 0 ? "" : rest[(sp2 + 1)..];
                if (cmd == "SETX") input = Escaping.Unescape(input);
                // Inside a batch the address text is buffered as given (a bare SET buffers ""), so END's single
                // ApplyBatch validates every address and length and applies nothing on failure (§9.3, §10.3).
                if (_batchOpen) { _pending.Add(new(addrText, input)); break; }
                if (!TryAddr(addrText, out var a)) return;
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
                _wb.MarkSaved();
                break;
            case "LOAD":
                if (rest.Length == 0) { Err("LOAD requires a path"); break; }
                _wb = GridlineFile.LoadFromPath(rest);
                break;
            case "NAME":
            {
                var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) { Err("usage: NAME <name> <refersTo>"); break; }
                _wb.DefineName(parts[0], parts[1]);
                break;
            }
            case "UNNAME":
            {
                var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 1) { Err("usage: UNNAME <name>"); break; }
                _wb.DeleteName(parts[0]);
                break;
            }
            case "NAMES":
            {
                var sb = new StringBuilder();
                foreach (var n in _wb.GetNames()) sb.Append("NAME\t").Append(n.Key).Append('\t').Append(n.Value).Append('\n');
                _out.Write(sb.ToString());
                break;
            }
            case "INSERTROWS": case "DELETEROWS": case "INSERTCOLS": case "DELETECOLS":
            {
                // <row> <n> or <letters> <n>; exactly two single-space-separated arguments (§10.3).
                var parts = rest.Split(' ');
                bool cols = cmd is "INSERTCOLS" or "DELETECOLS";
                int at = 0;
                if (parts.Length != 2
                    || !(cols ? ColumnName.TryParse(parts[0], out at) && at <= CellAddress.MaxColumns : TryPositiveInt(parts[0], out at))
                    || !TryPositiveInt(parts[1], out int n))
                {
                    Err("usage: " + cmd + (cols ? " <col> <n>" : " <row> <n>"));
                    break;
                }
                switch (cmd)
                {
                    case "INSERTROWS": _wb.InsertRows(at, n); break;
                    case "DELETEROWS": _wb.DeleteRows(at, n); break;
                    case "INSERTCOLS": _wb.InsertColumns(at, n); break;
                    default: _wb.DeleteColumns(at, n); break;
                }
                break;
            }
            default:
                Err("unknown command: " + (sp < 0 ? line : line[..sp]));
                break;
        }
    }

    /// <summary>ASCII digits only (no sign, no whitespace), value 1..int.MaxValue.</summary>
    private static bool TryPositiveInt(string s, out int value)
    {
        value = 0;
        if (s.Length == 0) return false;
        long v = 0;
        foreach (char c in s)
        {
            if (c is < '0' or > '9') return false;
            v = v * 10 + (c - '0');
            if (v > int.MaxValue) return false;
        }
        value = (int)v;
        return v >= 1;
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
