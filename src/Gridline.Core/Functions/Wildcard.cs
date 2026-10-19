namespace Gridline.Core.Functions;

/// <summary>
/// Case-insensitive wildcard matching with <c>*</c> (any sequence), <c>?</c> (any one char) and <c>~</c> (escape next char),
/// shared by SEARCH (§7.3) and the lookup/criteria functions (§7.4, §7.5). Iterative two-pointer matcher with
/// single-star backtracking: no regex, no recursion, O(pattern * text) worst case.
/// </summary>
internal static class Wildcard
{
    /// <summary>True if <paramref name="pattern"/> matches the whole of <paramref name="text"/>.</summary>
    public static bool IsMatch(string pattern, string text) => MatchAt(pattern, text);

    /// <summary>
    /// 0-based index of the first position at or after <paramref name="startIndex0"/> where the pattern matches a
    /// prefix of the remaining text (an empty match at <c>text.Length</c> counts); -1 if none.
    /// </summary>
    /// <remarks>
    /// The pattern is split at unescaped stars into segments of single-character tokens. The first segment must match
    /// at the start position; the remaining segments are then found greedily, leftmost, in order. Whether that tail
    /// succeeds only gets harder as the start moves right, so one failure ends the search: O(pattern * text) overall.
    /// </remarks>
    public static int Search(string pattern, string text, int startIndex0)
    {
        if (startIndex0 < 0) startIndex0 = 0;
        var segs = Split(pattern);
        var head = segs[0];
        for (int i = startIndex0; i <= text.Length; i++)
        {
            if (!MatchSeg(head, text, i)) continue;
            int pos = i + head.Count;
            for (int k = 1; k < segs.Count; k++)
            {
                int hit = FindSeg(segs[k], text, pos);
                if (hit < 0) return -1; // the tail cannot succeed from any later start either
                pos = hit + segs[k].Count;
            }
            return i;
        }
        return -1;
    }

    /// <summary>Splits at unescaped stars. Element 0 is the part before the first star (maybe empty); later empty parts are dropped.</summary>
    private static List<List<(char C, bool Any)>> Split(string p)
    {
        var segs = new List<List<(char C, bool Any)>> { new() };
        for (int pi = 0; pi < p.Length; pi++)
        {
            char c = p[pi];
            if (c == '*')
            {
                if (segs[^1].Count > 0 || segs.Count == 1) segs.Add(new());
                continue;
            }
            if (c == '?') segs[^1].Add((' ', true));
            else if (c == '~' && pi + 1 < p.Length) segs[^1].Add((char.ToUpperInvariant(p[++pi]), false));
            else segs[^1].Add((char.ToUpperInvariant(c), false));
        }
        if (segs.Count > 1 && segs[^1].Count == 0) segs.RemoveAt(segs.Count - 1);
        return segs;
    }

    private static bool MatchSeg(List<(char C, bool Any)> seg, string t, int at)
    {
        if (at + seg.Count > t.Length) return false;
        for (int k = 0; k < seg.Count; k++)
            if (!seg[k].Any && seg[k].C != char.ToUpperInvariant(t[at + k])) return false;
        return true;
    }

    private static int FindSeg(List<(char C, bool Any)> seg, string t, int from)
    {
        for (int at = from; at + seg.Count <= t.Length; at++)
            if (MatchSeg(seg, t, at)) return at;
        return -1;
    }

    private static bool MatchAt(string p, string t)
    {
        int ti = 0, pi = 0, starPi = -1, starTi = 0;
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
                if (ti == t.Length) return true;
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
