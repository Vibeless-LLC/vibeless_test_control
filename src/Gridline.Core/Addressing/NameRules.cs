namespace Gridline.Core.Addressing;

/// <summary>Defined-name syntax and <c>refersTo</c> rules (§9.6). Ordinal and culture-invariant.</summary>
public static class NameRules
{
    public const int MaxNameLength = 255;

    /// <summary>
    /// True when <paramref name="name"/> matches <c>^[A-Za-z_][A-Za-z0-9_.]{0,254}$</c>, is not <c>TRUE</c> or
    /// <c>FALSE</c> (case-insensitive), and does not look like a reference (<c>^[A-Za-z]{1,3}[0-9]+$</c>).
    /// </summary>
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
        if (!(IsLetter(name[0]) || name[0] == '_')) return false;
        for (int i = 1; i < name.Length; i++)
        {
            char c = name[i];
            if (!(IsLetter(c) || IsDigit(c) || c == '_' || c == '.')) return false;
        }
        if (name.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || name.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return false;
        return !LooksLikeReference(name);
    }

    /// <summary>
    /// Parses a fully absolute cell (<c>$B$1</c>) or range (<c>$A$1:$C$10</c>) within grid bounds, letters in any
    /// case and corners in any order. The result is normalized; a range whose corners coincide is a single cell.
    /// </summary>
    public static bool TryParseRefersTo(string refersTo, out RangeAddress target)
    {
        target = default;
        if (string.IsNullOrEmpty(refersTo)) return false;
        var s = refersTo.AsSpan();
        int colon = s.IndexOf(':');
        CellAddress a, b;
        if (colon < 0)
        {
            if (!TryParseAbsolute(s, out a)) return false;
            b = a;
        }
        else if (!TryParseAbsolute(s[..colon], out a) || !TryParseAbsolute(s[(colon + 1)..], out b))
            return false;
        target = new RangeAddress(
            new CellAddress(Math.Min(a.Row, b.Row), Math.Min(a.Col, b.Col)),
            new CellAddress(Math.Max(a.Row, b.Row), Math.Max(a.Col, b.Col)));
        return true;
    }

    /// <summary>Canonical uppercase form: <c>$B$1</c> for a single cell, <c>$A$1:$C$10</c> for a range.</summary>
    public static string FormatRefersTo(RangeAddress target)
    {
        static string Abs(CellAddress c) =>
            "$" + ColumnName.ToLetters(c.Col) + "$" + c.Row.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return target.TopLeft == target.BottomRight ? Abs(target.TopLeft) : Abs(target.TopLeft) + ":" + Abs(target.BottomRight);
    }

    /// <summary>As above, but with <paramref name="asRange"/> a 1x1 target keeps its range form (<c>$A$1:$A$1</c>).</summary>
    public static string FormatRefersTo(RangeAddress target, bool asRange)
    {
        if (!asRange) return FormatRefersTo(target);
        string a = FormatRefersTo(new RangeAddress(target.TopLeft, target.TopLeft));
        return target.TopLeft == target.BottomRight ? a + ":" + a : FormatRefersTo(target);
    }

    /// <summary><c>$LETTERS$DIGITS</c>, then the ordinary in-bounds cell address rules.</summary>
    private static bool TryParseAbsolute(ReadOnlySpan<char> s, out CellAddress a)
    {
        a = default;
        if (s.Length < 4 || s[0] != '$') return false;
        int i = 1;
        while (i < s.Length && IsLetter(s[i])) i++;
        if (i == 1 || i >= s.Length || s[i] != '$') return false;
        return CellAddress.TryParse(s, out a);
    }

    private static bool LooksLikeReference(string name)
    {
        int i = 0;
        while (i < name.Length && IsLetter(name[i])) i++;
        if (i is < 1 or > 3 || i == name.Length) return false;
        for (; i < name.Length; i++)
            if (!IsDigit(name[i])) return false;
        return true;
    }

    private static bool IsLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
