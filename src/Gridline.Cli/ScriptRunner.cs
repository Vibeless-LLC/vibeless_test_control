using System.Globalization;
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
                if (!ScriptArgs.TrySplit(rest, out var parts) || parts.Count != 2) { Err("PASTE requires <srcRange> <dstAddr>"); break; }
                _wb.CopyPaste(parts[0], parts[1]);
                break;
            }
            case "SET":
            case "SETX":
            {
                // The address may be a quoted qualified address ('My Data'!A1); the input follows verbatim (CR1.7).
                var (addrText, input) = ScriptArgs.SplitFirst(rest);
                if (cmd == "SETX") input = Escaping.Unescape(input);
                // Inside a batch the address text is buffered as given (a bare SET buffers ""), so END's single
                // ApplyBatch validates every address and length and applies nothing on failure (§9.3, §10.3).
                if (_batchOpen) { _pending.Add(new(addrText, input)); break; }
                if (!TryAddr(addrText, out var a)) return;
                _wb.SetCell(a, input);
                break;
            }
            case "GET":
                if (TryAddr(ScriptArgs.SplitFirst(rest).token, out var g))
                    _out.Write(g + "\t" + _wb.GetValue(g).ToCanonicalString() + "\n");
                break;
            case "INPUT":
                if (TryAddr(ScriptArgs.SplitFirst(rest).token, out var i))
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
                // Quote-aware so a refersTo like 'My Data'!$A$1 is one argument (CR1.7); empty tokens are skipped as before.
                if (!ScriptArgs.TrySplit(rest, out var raw)) { Err("usage: NAME <name> <refersTo>"); break; }
                var parts = raw.FindAll(t => t.Length > 0);
                if (parts.Count != 2) { Err("usage: NAME <name> <refersTo>"); break; }
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
                // [<sheet>!]<row> <n> or [<sheet>!]<letters> <n>; two single-space-separated arguments (§10.3, CR1.7).
                bool cols = cmd is "INSERTCOLS" or "DELETECOLS";
                int at = 0;
                string? sheet = null;
                if (!ScriptArgs.TrySplit(rest, out var parts) || parts.Count != 2
                    || !QualifiedAddress.TryParseColumnOrRow(parts[0], cols, out sheet, out at)
                    || (cols && at > CellAddress.MaxColumns)
                    || !TryPositiveInt(parts[1], out int n))
                {
                    Err("usage: " + cmd + (cols ? " [<sheet>!]<col> <n>" : " [<sheet>!]<row> <n>"));
                    break;
                }
                string target = sheet ?? _wb.ActiveSheet;
                switch (cmd)
                {
                    case "INSERTROWS": _wb.InsertRows(target, at, n); break;
                    case "DELETEROWS": _wb.DeleteRows(target, at, n); break;
                    case "INSERTCOLS": _wb.InsertColumns(target, at, n); break;
                    default: _wb.DeleteColumns(target, at, n); break;
                }
                break;
            }
            case "ADDSHEET":
            {
                if (!SheetArgs(rest, 1, 1, 2, out var a) || (a.Count == 2 && !TryNonNegativeInt(a[1], out _)))
                {
                    Err("usage: ADDSHEET <name> [index]");
                    break;
                }
                int? index = a.Count == 2 ? int.Parse(a[1], CultureInfo.InvariantCulture) : null;
                _wb.AddSheet(a[0], index);
                break;
            }
            case "RENAMESHEET":
                if (!SheetArgs(rest, 2, 2, 2, out var rn)) { Err("usage: RENAMESHEET <old> <new>"); break; }
                _wb.RenameSheet(rn[0], rn[1]);
                break;
            case "DELETESHEET":
                if (!SheetArgs(rest, 1, 1, 1, out var dn)) { Err("usage: DELETESHEET <name>"); break; }
                _wb.DeleteSheet(dn[0]);
                break;
            case "MOVESHEET":
            {
                if (!SheetArgs(rest, 1, 2, 2, out var mv) || !TryNonNegativeInt(mv[1], out int to))
                {
                    Err("usage: MOVESHEET <name> <index>");
                    break;
                }
                _wb.MoveSheet(mv[0], to);
                break;
            }
            case "ACTIVATE":
                if (!SheetArgs(rest, 1, 1, 1, out var an)) { Err("usage: ACTIVATE <name>"); break; }
                _wb.ActiveSheet = an[0];
                break;
            case "SHEETS":
            {
                if (sp >= 0) { Err("usage: SHEETS"); break; }
                var sb = new StringBuilder();
                var names = _wb.SheetNames;
                for (int k = 0; k < names.Count; k++)
                    sb.Append("SHEET\t").Append(k.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(Escaping.Escape(names[k])).Append('\n');
                sb.Append("ACTIVE\t").Append(Escaping.Escape(_wb.ActiveSheet)).Append('\n');
                _out.Write(sb.ToString());
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

    /// <summary>ASCII digits only, value 0..int.MaxValue (a sheet index).</summary>
    private static bool TryNonNegativeInt(string s, out int value)
    {
        value = 0;
        return s == "0" || TryPositiveInt(s, out value);
    }

    /// <summary>
    /// Splits <paramref name="rest"/> into <paramref name="min"/>..<paramref name="max"/> arguments, the first
    /// <paramref name="names"/> of which must be sheet names per CR1.2 quoting (<see cref="SheetNames.TryParseName"/>);
    /// those are replaced by the unquoted names.
    /// </summary>
    private static bool SheetArgs(string rest, int names, int min, int max, out List<string> args)
    {
        if (!ScriptArgs.TrySplit(rest, out args) || args.Count < min || args.Count > max) return false;
        for (int k = 0; k < names; k++)
        {
            if (!SheetNames.TryParseName(args[k], out var n)) return false;
            args[k] = n;
        }
        return true;
    }

    /// <summary>Canonical echo of a cell address (CR1.7); a syntax error prints ERR, an unknown sheet throws (→ ERR).</summary>
    private bool TryAddr(string text, out string canonical)
    {
        if (ScriptArgs.TryCanonical(_wb, text, out canonical)) return true;
        Err("invalid address: " + text);
        return false;
    }
}
