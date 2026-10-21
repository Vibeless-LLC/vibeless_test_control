using Gridline.Core;
using Gridline.Core.Addressing;

namespace Gridline.Cli;

/// <summary>Argument splitting and canonical address echo for the script language and CLI (§10.3, CR1.7).</summary>
internal static class ScriptArgs
{
    /// <summary>
    /// Splits on single spaces outside <c>'…'</c> (CR1.2 quoting): a space inside quotes belongs to the token, and
    /// <c>''</c> inside quotes is part of it too. Tokens keep their quotes. Without quotes the result equals
    /// <c>rest.Split(' ')</c> (so <c>""</c> gives one empty token). An unterminated quote fails.
    /// </summary>
    public static bool TrySplit(string rest, out List<string> args)
    {
        args = [];
        int start = 0;
        bool quoted = false;
        for (int i = 0; i < rest.Length; i++)
        {
            char c = rest[i];
            if (c == '\'')
            {
                // Inside quotes, '' is an escaped apostrophe; it toggles twice, which is the same as staying quoted.
                quoted = !quoted;
            }
            else if (c == ' ' && !quoted)
            {
                args.Add(rest[start..i]);
                start = i + 1;
            }
        }
        if (quoted) { args = []; return false; }
        args.Add(rest[start..]);
        return true;
    }

    /// <summary>
    /// The first quote-aware token and everything after the space that ends it (verbatim). With an unterminated
    /// quote the whole text is the token (and so an invalid address) and the remainder is empty.
    /// </summary>
    public static (string token, string remainder) SplitFirst(string rest)
    {
        bool quoted = false;
        for (int i = 0; i < rest.Length; i++)
        {
            char c = rest[i];
            if (c == '\'') quoted = !quoted;
            else if (c == ' ' && !quoted) return (rest[..i], rest[(i + 1)..]);
        }
        return (rest, "");
    }

    /// <summary>
    /// Echoes a cell address canonically (CR1.7): <c>A1</c> when unqualified, else the sheet in its defined casing,
    /// quoted only when required, then <c>!A1</c>. False on a syntax error; an unknown sheet throws
    /// <see cref="ArgumentException"/>.
    /// </summary>
    public static bool TryCanonical(Workbook wb, string text, out string canonical)
    {
        canonical = "";
        if (!QualifiedAddress.TryParseCell(text, out var sheet, out var cell)) return false;
        canonical = sheet is null ? cell.ToString() : SheetNames.Format(DefinedName(wb, sheet)) + "!" + cell;
        return true;
    }

    /// <summary>The sheet's name in its defined casing (case-insensitive lookup); unknown → <see cref="ArgumentException"/>.</summary>
    public static string DefinedName(Workbook wb, string sheet)
    {
        foreach (var n in wb.SheetNames)
            if (string.Equals(n, sheet, StringComparison.OrdinalIgnoreCase)) return n;
        throw new ArgumentException("unknown sheet: " + sheet);
    }
}
