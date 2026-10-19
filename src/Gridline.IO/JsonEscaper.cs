using System.Text;

namespace Gridline.IO;

/// <summary>Canonical JSON string escaping (§8.1). Everything not listed is written literally.</summary>
internal static class JsonEscaper
{
    private const string Hex = "0123456789abcdef";

    /// <summary>Appends <paramref name="s"/> as a quoted JSON string.</summary>
    public static void Append(StringBuilder sb, string s)
    {
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        AppendU(sb, c);
                    }
                    else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        sb.Append(c).Append(s[++i]);
                    }
                    else if (char.IsSurrogate(c))
                    {
                        // Lone surrogate: cannot be UTF-8 encoded, so escape it (lowercase hex).
                        AppendU(sb, c);
                    }
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private static void AppendU(StringBuilder sb, char c)
    {
        sb.Append("\\u").Append(Hex[(c >> 12) & 0xF]).Append(Hex[(c >> 8) & 0xF]).Append(Hex[(c >> 4) & 0xF]).Append(Hex[c & 0xF]);
    }
}
