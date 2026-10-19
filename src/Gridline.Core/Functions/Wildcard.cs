namespace Gridline.Core.Functions;

/// <summary>
/// Case-insensitive wildcard matching with <c>*</c> (any sequence), <c>?</c> (any one char) and <c>~</c> (escape next char),
/// shared by SEARCH (§7.3) and the lookup/criteria functions (§7.4, §7.5). Iterative two-pointer matcher with
/// single-star backtracking: no regex, no recursion, O(pattern * text) worst case.
/// </summary>
internal static class Wildcard
{
    /// <summary>True if <paramref name="pattern"/> matches the whole of <paramref name="text"/>.</summary>
    public static bool IsMatch(string pattern, string text) => MatchAt(pattern, text, 0, whole: true);

    /// <summary>
    /// 0-based index of the first position at or after <paramref name="startIndex0"/> where the pattern matches a
    /// prefix of the remaining text (an empty match at <c>text.Length</c> counts); -1 if none.
    /// </summary>
    public static int Search(string pattern, string text, int startIndex0)
    {
        if (startIndex0 < 0) startIndex0 = 0;
        for (int i = startIndex0; i <= text.Length; i++)
            if (MatchAt(pattern, text, i, whole: false)) return i;
        return -1;
    }

    private static bool MatchAt(string p, string t, int ti, bool whole)
    {
        int pi = 0, starPi = -1, starTi = 0;
        while (true)
        {
            if (pi < p.Length && p[pi] == '*')
            {
                starPi = pi++;
                starTi = ti;
                continue;
            }
            if (pi == p.Length)
            {
                if (!whole || ti == t.Length) return true;
            }
            else if (ti < t.Length)
            {
                char pc = p[pi];
                int width = 1;
                bool any = false;
                if (pc == '?') any = true;
                else if (pc == '~' && pi + 1 < p.Length) { pc = p[pi + 1]; width = 2; }
                if (any || char.ToUpperInvariant(pc) == char.ToUpperInvariant(t[ti]))
                {
                    pi += width;
                    ti++;
                    continue;
                }
            }
            // Mismatch (or leftover text): let the last star absorb one more character.
            if (starPi < 0 || starTi >= t.Length) return false;
            starTi++;
            ti = starTi;
            pi = starPi + 1;
        }
    }
}
