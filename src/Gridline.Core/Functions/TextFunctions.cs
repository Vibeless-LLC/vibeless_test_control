using System.Text;
using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>The 17 text functions (§7.3). Positions and lengths are 1-based UTF-16 code units.</summary>
internal static class TextFunctions
{
    private const int MaxText = 32767;

    public static void Register(FunctionRegistry r)
    {
        Add(r, "LEN", 1, 1, a => WithText(a[0], t => CellValue.FromNumber(t.Length)));
        Add(r, "UPPER", 1, 1, a => WithText(a[0], t => Text(t.ToUpperInvariant())));
        Add(r, "LOWER", 1, 1, a => WithText(a[0], t => Text(t.ToLowerInvariant())));
        Add(r, "PROPER", 1, 1, a => WithText(a[0], t => Text(Proper(t))));
        Add(r, "TRIM", 1, 1, a => WithText(a[0], t => Text(Trim(t))));
        Add(r, "LEFT", 1, 2, a => Side(a, left: true));
        Add(r, "RIGHT", 1, 2, a => Side(a, left: false));
        Add(r, "MID", 3, 3, Mid);
        Add(r, "CONCAT", 1, 255, a => Join(a, 0, "", ignoreEmpty: false, sparseBlanks: false));
        Add(r, "TEXTJOIN", 3, 255, TextJoin);
        Add(r, "FIND", 2, 3, a => Find(a, wildcard: false));
        Add(r, "SEARCH", 2, 3, a => Find(a, wildcard: true));
        Add(r, "SUBSTITUTE", 3, 4, Substitute);
        Add(r, "REPLACE", 4, 4, Replace);
        Add(r, "REPT", 2, 2, Rept);
        Add(r, "EXACT", 2, 2, a => WithText(a[0], x => WithText(a[1], y => CellValue.FromBoolean(string.Equals(x, y, StringComparison.Ordinal)))));
        Add(r, "VALUE", 1, 1, a => Coercion.ToNumber(a[0].Evaluate()));
    }

    private static void Add(FunctionRegistry r, string name, int min, int max, Func<IReadOnlyList<Arg>, CellValue> f) =>
        r.Register(name, min, max, new DelegateFunction((a, _) => f(a)));

    private static CellValue Text(string s) =>
        s.Length > MaxText ? CellValue.FromError(ErrorCodes.Value) : CellValue.FromText(s);

    private static CellValue WithText(Arg a, Func<string, CellValue> f)
    {
        var t = Coercion.ToText(a.Evaluate());
        return t.Kind == ValueKind.Error ? t : f(t.Text);
    }

    /// <summary>Integer argument; an omitted/absent optional argument yields <paramref name="dflt"/>.</summary>
    private static bool TryInt(IReadOnlyList<Arg> a, int index, int dflt, out int value, out CellValue error)
    {
        if (index >= a.Count || a[index].IsOmitted) { value = dflt; error = default; return true; }
        return Coercion.TryToInt(a[index].Evaluate(), out value, out error);
    }

    private static string Proper(string t)
    {
        var sb = new StringBuilder(t.Length);
        bool prevLetter = false;
        foreach (char c in t)
        {
            bool letter = char.IsLetter(c);
            sb.Append(letter ? (prevLetter ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)) : c);
            prevLetter = letter;
        }
        return sb.ToString();
    }

    private static string Trim(string t)
    {
        var sb = new StringBuilder(t.Length);
        bool pendingSpace = false;
        foreach (char c in t)
        {
            if (c == ' ') { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static CellValue Side(IReadOnlyList<Arg> a, bool left) =>
        WithText(a[0], t =>
        {
            if (!TryInt(a, 1, 1, out int n, out var err)) return err;
            if (n < 0) return CellValue.FromError(ErrorCodes.Value);
            if (n >= t.Length) return CellValue.FromText(t);
            return CellValue.FromText(left ? t[..n] : t[^n..]);
        });

    private static CellValue Mid(IReadOnlyList<Arg> a) =>
        WithText(a[0], t =>
        {
            if (!TryInt(a, 1, 0, out int s, out var e1)) return e1;
            if (!TryInt(a, 2, 0, out int n, out var e2)) return e2;
            if (s < 1 || n < 0) return CellValue.FromError(ErrorCodes.Value);
            if (s > t.Length) return CellValue.FromText("");
            int start = s - 1;
            return CellValue.FromText(t.Substring(start, Math.Min(n, t.Length - start)));
        });

    /// <summary>Joins values from <paramref name="args"/>[<paramref name="first"/>..]; ranges are read row-major.</summary>
    private static CellValue Join(IReadOnlyList<Arg> args, int first, string delim, bool ignoreEmpty, bool sparseBlanks)
    {
        var sb = new StringBuilder();
        bool any = false;

        bool Append(string s)
        {
            if (any) sb.Append(delim);
            any = true;
            sb.Append(s);
            return sb.Length <= MaxText;
        }

        bool AppendBlanks(long count)
        {
            if (count <= 0) return true;
            if (delim.Length == 0) { any = true; return true; }
            for (long k = 0; k < count; k++)
                if (!Append("")) return false;
            return true;
        }

        var tooLong = CellValue.FromError(ErrorCodes.Value);
        for (int i = first; i < args.Count; i++)
        {
            var a = args[i];
            if (a.Range is { } range)
            {
                long prev = -1; // linear row-major index of the last cell consumed
                var top = range.Address.TopLeft;
                foreach (var (row, col, v) in range.Ctx.EnumerateNonBlank(range.Address))
                {
                    long idx = (long)(row - top.Row) * range.Cols + (col - top.Col);
                    if (sparseBlanks && !ignoreEmpty && !AppendBlanks(idx - prev - 1)) return tooLong;
                    prev = idx;
                    if (v.Kind == ValueKind.Error) return v;
                    var t = Coercion.ToText(v).Text;
                    if (ignoreEmpty && t.Length == 0) continue;
                    if (!Append(t)) return tooLong;
                }
                if (sparseBlanks && !ignoreEmpty && !AppendBlanks((long)range.CellCount - 1 - prev)) return tooLong;
            }
            else
            {
                var v = a.Evaluate();
                if (v.Kind == ValueKind.Error) return v;
                var t = Coercion.ToText(v).Text;
                if (ignoreEmpty && t.Length == 0) continue;
                if (!Append(t)) return tooLong;
            }
        }
        return CellValue.FromText(sb.ToString());
    }

    private static CellValue TextJoin(IReadOnlyList<Arg> a)
    {
        var d = Coercion.ToText(a[0].Evaluate());
        if (d.Kind == ValueKind.Error) return d;
        var ig = Coercion.ToBoolean(a[1].Evaluate());
        if (ig.Kind == ValueKind.Error) return ig;
        return Join(a, 2, d.Text, ig.Boolean, sparseBlanks: true);
    }

    private static CellValue Find(IReadOnlyList<Arg> a, bool wildcard)
    {
        var f = Coercion.ToText(a[0].Evaluate());
        if (f.Kind == ValueKind.Error) return f;
        return WithText(a[1], t =>
        {
            if (!TryInt(a, 2, 1, out int s, out var err)) return err;
            if (s < 1 || s > t.Length + 1) return CellValue.FromError(ErrorCodes.Value);
            int idx = wildcard
                ? Wildcard.Search(f.Text, t, s - 1)
                : t.IndexOf(f.Text, s - 1, StringComparison.Ordinal);
            return idx < 0 ? CellValue.FromError(ErrorCodes.Value) : CellValue.FromNumber(idx + 1);
        });
    }

    private static CellValue Substitute(IReadOnlyList<Arg> a) =>
        WithText(a[0], t => WithText(a[1], old => WithText(a[2], rep =>
        {
            int which = 0; // 0 = all occurrences
            if (a.Count > 3 && !a[3].IsOmitted)
            {
                if (!Coercion.TryToInt(a[3].Evaluate(), out which, out var err)) return err;
                if (which < 1) return CellValue.FromError(ErrorCodes.Value);
            }
            if (old.Length == 0) return CellValue.FromText(t);

            var sb = new StringBuilder();
            int pos = 0, seen = 0;
            while (true)
            {
                int hit = t.IndexOf(old, pos, StringComparison.Ordinal);
                if (hit < 0) break;
                seen++;
                if (which == 0 || seen == which)
                {
                    sb.Append(t, pos, hit - pos).Append(rep);
                    if (sb.Length > MaxText) return CellValue.FromError(ErrorCodes.Value);
                    pos = hit + old.Length;
                    if (which != 0) break;
                }
                else
                {
                    sb.Append(t, pos, hit + old.Length - pos);
                    pos = hit + old.Length;
                }
            }
            sb.Append(t, pos, t.Length - pos);
            return Text(sb.ToString());
        })));

    private static CellValue Replace(IReadOnlyList<Arg> a) =>
        WithText(a[0], t =>
        {
            if (!TryInt(a, 1, 0, out int s, out var e1)) return e1;
            if (!TryInt(a, 2, 0, out int n, out var e2)) return e2;
            if (s < 1 || n < 0) return CellValue.FromError(ErrorCodes.Value);
            return WithText(a[3], rep =>
            {
                int start = Math.Min(s - 1, t.Length);
                int remove = Math.Min(n, t.Length - start);
                return Text(string.Concat(t.AsSpan(0, start), rep, t.AsSpan(start + remove)));
            });
        });

    private static CellValue Rept(IReadOnlyList<Arg> a) =>
        WithText(a[0], t =>
        {
            if (!TryInt(a, 1, 0, out int n, out var err)) return err;
            if (n < 0) return CellValue.FromError(ErrorCodes.Value);
            if (t.Length == 0 || n == 0) return CellValue.FromText("");
            if ((long)t.Length * n > MaxText) return CellValue.FromError(ErrorCodes.Value);
            return CellValue.FromText(new StringBuilder(t.Length * n).Insert(0, t, n).ToString());
        });
}
