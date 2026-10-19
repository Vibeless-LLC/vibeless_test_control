using System.Text;

namespace Gridline.Cli;

/// <summary>Text escaping for CLI output and SETX input (§8.3, §10.3).</summary>
internal static class Escaping
{
    /// <summary>Escapes backslash, tab, LF and CR.</summary>
    public static string Escape(string s)
    {
        if (s.AsSpan().IndexOfAny("\\\t\n\r".AsSpan()) < 0) return s;
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Reverses <see cref="Escape"/>. An unknown escape (or a trailing lone backslash) is left verbatim.</summary>
    public static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                char? r = n switch { '\\' => '\\', 't' => '\t', 'n' => '\n', 'r' => '\r', _ => null };
                if (r is char rc) { sb.Append(rc); i++; continue; }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
