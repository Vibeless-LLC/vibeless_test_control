using Gridline.Core.Addressing;

namespace Gridline.Core.Parsing;

/// <summary>Converts a formula body (without the leading '=') into tokens (§5.1).</summary>
internal static class Tokenizer
{
    private static readonly string[] ErrorsLongestFirst =
        ErrorCodes.All.OrderByDescending(e => e.Length).ThenBy(e => e, StringComparer.Ordinal).ToArray();

    /// <summary>Tokenizes <paramref name="formulaBody"/>; returns false on any lexical error.</summary>
    public static bool TryTokenize(string formulaBody, out List<Token> tokens)
    {
        tokens = new List<Token>();
        string s = formulaBody ?? "";
        int i = 0, n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (c == ' ' || c == '\t') { i++; continue; }
            int start = i;

            if (SheetNames.TryParsePrefix(s.AsSpan(), i, out string sheet, out int afterBang))
            {
                // CR1.2: a prefix must be immediately followed by a cell reference (no whitespace, never a name or call).
                i = afterBang;
                if (i >= n || !(s[i] == '$' || IsLetter(s[i])) || !TryScanWord(s, ref i, out _)) return false;
                string refText = s[afterBang..i];
                if ((i < n && s[i] == '(') || !CellAddress.TryParse(refText, out _)) return false;
                tokens.Add(new Token(TokenKind.SheetPrefix, sheet, start));
                tokens.Add(new Token(TokenKind.CellRef, refText, afterBang));
            }
            else if (IsDigit(c) || (c == '.' && i + 1 < n && IsDigit(s[i + 1])))
            {
                i = ScanNumber(s, i);
                tokens.Add(new Token(TokenKind.Number, s[start..i], start));
            }
            else if (c == '"')
            {
                i++;
                var sb = new System.Text.StringBuilder();
                bool closed = false;
                while (i < n)
                {
                    if (s[i] == '"')
                    {
                        if (i + 1 < n && s[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        i++; closed = true; break;
                    }
                    sb.Append(s[i++]);
                }
                if (!closed) return false;
                tokens.Add(new Token(TokenKind.String, sb.ToString(), start));
            }
            else if (c == '#')
            {
                string? hit = null;
                foreach (var e in ErrorsLongestFirst)
                    if (string.CompareOrdinal(s, i, e, 0, e.Length) == 0) { hit = e; break; }
                if (hit is null) return false;
                i += hit.Length;
                tokens.Add(new Token(TokenKind.Error, hit, start));
            }
            else if (c == '$' || IsLetter(c))
            {
                if (!TryScanWord(s, ref i, out bool hasDollar)) return false;
                string word = s[start..i];
                if (i < n && s[i] == '(')
                {
                    if (hasDollar) return false;
                    tokens.Add(new Token(TokenKind.Function, word, start));
                }
                else if (CellAddress.TryParse(word, out _))
                    tokens.Add(new Token(TokenKind.CellRef, word, start));
                else if (hasDollar) return false;
                else if (word.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || word.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                    tokens.Add(new Token(TokenKind.Bool, word, start));
                else
                    tokens.Add(new Token(TokenKind.Name, word, start));
            }
            else
            {
                i++;
                switch (c)
                {
                    case '(': tokens.Add(new Token(TokenKind.LParen, "(", start)); break;
                    case ')': tokens.Add(new Token(TokenKind.RParen, ")", start)); break;
                    case ',': tokens.Add(new Token(TokenKind.Comma, ",", start)); break;
                    case ':': case '+': case '-': case '*': case '/': case '^': case '&': case '%': case '=':
                        tokens.Add(new Token(TokenKind.Op, c.ToString(), start)); break;
                    case '<':
                        if (i < n && (s[i] == '=' || s[i] == '>')) { tokens.Add(new Token(TokenKind.Op, s.Substring(start, 2), start)); i++; }
                        else tokens.Add(new Token(TokenKind.Op, "<", start));
                        break;
                    case '>':
                        if (i < n && s[i] == '=') { tokens.Add(new Token(TokenKind.Op, ">=", start)); i++; }
                        else tokens.Add(new Token(TokenKind.Op, ">", start));
                        break;
                    default: return false;
                }
            }
        }
        tokens.Add(new Token(TokenKind.End, "", n));
        return true;
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';
    private static bool IsLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static int ScanNumber(string s, int i)
    {
        int n = s.Length;
        while (i < n && IsDigit(s[i])) i++;
        if (i < n && s[i] == '.') { i++; while (i < n && IsDigit(s[i])) i++; }
        if (i < n && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < n && (s[j] == '+' || s[j] == '-')) j++;
            if (j < n && IsDigit(s[j])) { while (j < n && IsDigit(s[j])) j++; i = j; }
        }
        return i;
    }

    // Word: optional '$', a letter, then [A-Za-z0-9_.]*; if '$' follows, '$' plus digits.
    private static bool TryScanWord(string s, ref int i, out bool hasDollar)
    {
        int n = s.Length;
        hasDollar = false;
        if (s[i] == '$') { hasDollar = true; i++; }
        if (i >= n || !IsLetter(s[i])) return false;
        while (i < n && (IsLetter(s[i]) || IsDigit(s[i]) || s[i] == '_' || s[i] == '.')) i++;
        if (i < n && s[i] == '$')
        {
            hasDollar = true;
            i++;
            int d = i;
            while (i < n && IsDigit(s[i])) i++;
            if (i == d) return false;
        }
        return true;
    }
}
