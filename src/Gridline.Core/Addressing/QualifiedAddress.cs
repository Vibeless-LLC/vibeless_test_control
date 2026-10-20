namespace Gridline.Core.Addressing;

/// <summary>A cell or range with an optional sheet prefix (<c>Sheet2!A1:B3</c>, <c>'My Sheet'!C5</c>) (CR1.2).</summary>
public readonly record struct QualifiedAddress(string? Sheet, RangeAddress Range, bool IsRange)
{
    /// <summary>Accepts <c>[prefix]A1</c> or <c>[prefix]A1:B3</c>; the range is normalized. One prefix only.</summary>
    public static bool TryParse(string? s, out QualifiedAddress a)
    {
        a = default;
        if (s is null || !SplitPrefix(s, out var sheet, out int start)) return false;
        string rest = s[start..];
        if (!RangeAddress.TryParse(rest, out var r)) return false;
        a = new QualifiedAddress(sheet, r, rest.Contains(':', StringComparison.Ordinal));
        return true;
    }

    /// <summary>Accepts <c>[prefix]A1</c> only (no range).</summary>
    public static bool TryParseCell(string? s, out string? sheet, out CellAddress cell)
    {
        sheet = null;
        cell = default;
        if (s is null || !SplitPrefix(s, out var sh, out int start)) return false;
        if (!CellAddress.TryParse(s.AsSpan(start), out cell)) return false;
        sheet = sh;
        return true;
    }

    /// <summary>Parses <c>[prefix]C</c> (letters up to ZZ) or <c>[prefix]5</c> (1..int.MaxValue).</summary>
    public static bool TryParseColumnOrRow(string s, bool column, out string? sheet, out int index)
    {
        sheet = null;
        index = 0;
        if (s is null || !SplitPrefix(s, out var sh, out int start)) return false;
        var rest = s.AsSpan(start);
        if (column)
        {
            if (!ColumnName.TryParse(rest, out index)) return false;
        }
        else
        {
            if (rest.Length == 0) return false;
            long v = 0;
            foreach (char c in rest)
            {
                if (c is < '0' or > '9') return false;
                v = v * 10 + (c - '0');
                if (v > int.MaxValue) return false;
            }
            if (v < 1) return false;
            index = (int)v;
        }
        sheet = sh;
        return true;
    }

    /// <summary>Strips an optional sheet prefix; false when the text starts like a prefix that is malformed.</summary>
    private static bool SplitPrefix(string s, out string? sheet, out int start)
    {
        sheet = null;
        start = 0;
        if (s.Length > 0 && s[0] == '\'')
        {
            if (!SheetNames.TryParsePrefix(s.AsSpan(), 0, out var n, out start)) return false;
            sheet = n;
            return true;
        }
        if (SheetNames.TryParsePrefix(s.AsSpan(), 0, out var name, out int after))
        {
            sheet = name;
            start = after;
        }
        return true;
    }
}
