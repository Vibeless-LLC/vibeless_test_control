using System.Text;

namespace Gridline.Core.Addressing;

/// <summary>Sheet-name validity (CR1.1) and quoting rules (CR1.2). Ordinal and culture-invariant.</summary>
public static class SheetNames
{
    public const int MaxLength = 31;
    public const int MaxSheets = 255;

    /// <summary>1–31 chars, none of <c>: \ / ? * [ ]</c>, not beginning or ending with an apostrophe.</summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength) return false;
        if (name[0] == '\'' || name[^1] == '\'') return false;
        foreach (char c in name)
            if (c is ':' or '\\' or '/' or '?' or '*' or '[' or ']') return false;
        return true;
    }

    /// <summary>
    /// True when the name must be quoted: empty, a char outside <c>[A-Za-z0-9_.]</c>, starts with a digit,
    /// looks like a cell reference (<c>^[A-Za-z]{1,3}[0-9]+$</c>), or is TRUE/FALSE (any case).
    /// </summary>
    public static bool NeedsQuoting(string name)
    {
        if (name.Length == 0) return true;
        foreach (char c in name)
            if (!IsPlain(c)) return true;
        if (IsDigit(name[0])) return true;
        if (name.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || name.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return true;
        return LooksLikeReference(name);
    }

    /// <summary>The name as written in a reference: quoted (with <c>''</c> for <c>'</c>) iff <see cref="NeedsQuoting"/>.</summary>
    public static string Format(string name) =>
        NeedsQuoting(name) ? "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'" : name;

    /// <summary>
    /// Reads <c>'…'!</c> (with <c>''</c> escapes) or an unquoted run of <c>[A-Za-z0-9_.]</c> that does not need quoting,
    /// followed by <c>!</c>, starting at <paramref name="start"/>. A quoted name may be any non-empty text.
    /// </summary>
    internal static bool TryParsePrefix(ReadOnlySpan<char> s, int start, out string name, out int afterBang)
    {
        name = "";
        afterBang = 0;
        if (start < 0 || start >= s.Length) return false;
        int i = start;
        if (s[i] == '\'')
        {
            var sb = new StringBuilder();
            i++;
            while (true)
            {
                if (i >= s.Length) return false;
                if (s[i] == '\'')
                {
                    if (i + 1 < s.Length && s[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                    i++;
                    break;
                }
                sb.Append(s[i++]);
            }
            if (sb.Length == 0 || i >= s.Length || s[i] != '!') return false;
            name = sb.ToString();
            afterBang = i + 1;
            return true;
        }
        while (i < s.Length && IsPlain(s[i])) i++;
        if (i == start || i >= s.Length || s[i] != '!') return false;
        string plain = s[start..i].ToString();
        if (NeedsQuoting(plain)) return false;
        name = plain;
        afterBang = i + 1;
        return true;
    }

    /// <summary>The whole token is one sheet name: quoted (<c>'My Sheet'</c>) or unquoted without needing quotes.</summary>
    public static bool TryParseName(string token, out string name)
    {
        name = "";
        if (string.IsNullOrEmpty(token)) return false;
        if (token[0] == '\'')
        {
            if (!TryParsePrefix((token + "!").AsSpan(), 0, out var n, out int after) || after != token.Length + 1) return false;
            name = n;
            return true;
        }
        if (NeedsQuoting(token)) return false;
        name = token;
        return true;
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

    private static bool IsPlain(char c) => IsLetter(c) || IsDigit(c) || c == '_' || c == '.';
    private static bool IsLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
