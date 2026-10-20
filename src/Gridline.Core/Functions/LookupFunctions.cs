using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>The 9 lookup and reference functions (§7.4).</summary>
internal static class LookupFunctions
{
    public static void Register(FunctionRegistry r)
    {
        Add(r, "VLOOKUP", 3, 4, (a, _) => Lookup(a, vertical: true));
        Add(r, "HLOOKUP", 3, 4, (a, _) => Lookup(a, vertical: false));
        Add(r, "INDEX", 2, 3, (a, _) => Index(a));
        Add(r, "MATCH", 2, 3, (a, _) => Match(a));
        Add(r, "CHOOSE", 2, 255, (a, _) => Choose(a));
        Add(r, "ROW", 0, 1, (a, ctx) => RowCol(a, ctx, row: true));
        Add(r, "COLUMN", 0, 1, (a, ctx) => RowCol(a, ctx, row: false));
        Add(r, "ROWS", 1, 1, (a, _) => Dimension(a[0], rows: true));
        Add(r, "COLUMNS", 1, 1, (a, _) => Dimension(a[0], rows: false));
    }

    private static void Add(FunctionRegistry r, string name, int min, int max, Func<IReadOnlyList<Arg>, IEvalContext, CellValue> f) =>
        r.Register(name, min, max, new DelegateFunction(f));

    private static CellValue Err(string code) => CellValue.FromError(code);

    /// <summary>The reference behind <paramref name="a"/>, or (null) with the error/#VALUE! to return when it is not one.</summary>
    private static RangeRef? AsRange(Arg a, out CellValue error)
    {
        error = default;
        if (a.Range is { } r) return r;
        var v = a.Evaluate();
        error = v.Kind == ValueKind.Error ? v : Err(ErrorCodes.Value);
        return null;
    }

    /// <summary>§7.4 exact equality: same kind only; numbers exactly, booleans by value, text case-insensitively with wildcards.</summary>
    private static bool ExactEqual(CellValue v, CellValue cell) => v.Kind == cell.Kind && v.Kind switch
    {
        ValueKind.Number => v.Number == cell.Number,
        ValueKind.Boolean => v.Boolean == cell.Boolean,
        ValueKind.Text => Wildcard.IsMatch(v.Text, cell.Text),
        _ => false, // Blank and Error never match
    };

    /// <summary>Index of the last element in [0, n) for which <paramref name="holds"/> is true, assuming it is true then false; -1 if none.</summary>
    private static int LastWhere(int n, Func<int, bool> holds)
    {
        int lo = 0, hi = n - 1, best = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (holds(mid)) { best = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return best;
    }

    /// <summary>Key ≤ v under §6.3; an error key never qualifies.</summary>
    private static bool KeyLe(CellValue key, CellValue v) => key.Kind != ValueKind.Error && Comparison.Compare(key, v) <= 0;

    private static bool KeyGe(CellValue key, CellValue v) => key.Kind != ValueKind.Error && Comparison.Compare(key, v) >= 0;

    private static CellValue Lookup(IReadOnlyList<Arg> a, bool vertical)
    {
        var v = ArgGuard.Required(a[0]);
        if (v.Kind == ValueKind.Error) return v;
        if (AsRange(a[1], out var err) is not { } table) return err;
        if (!Coercion.TryToInt(ArgGuard.Required(a[2]), out int index, out var ierr)) return ierr;
        bool approx = true;
        if (a.Count > 3 && !a[3].IsOmitted)
        {
            var b = Coercion.ToBoolean(a[3].Evaluate());
            if (b.Kind == ValueKind.Error) return b;
            approx = b.Boolean;
        }
        if (index < 1) return Err(ErrorCodes.Value);
        int span = vertical ? table.Cols : table.Rows;
        if (index > span) return Err(ErrorCodes.Ref);

        int n = vertical ? table.Rows : table.Cols;
        CellValue Key(int i) => vertical ? table.Get(i, 0) : table.Get(0, i);
        int hit = -1;
        if (approx) hit = LastWhere(n, i => KeyLe(Key(i), v));
        else
            for (int i = 0; i < n; i++)
                if (ExactEqual(v, Key(i))) { hit = i; break; }
        if (hit < 0) return Err(ErrorCodes.NA);
        return vertical ? table.Get(hit, index - 1) : table.Get(index - 1, hit);
    }

    private static CellValue Index(IReadOnlyList<Arg> a)
    {
        if (AsRange(a[0], out var err) is not { } range) return err;
        if (!Coercion.TryToInt(ArgGuard.Required(a[1]), out int r, out var rerr)) return rerr;
        bool hasC = a.Count > 2 && !a[2].IsOmitted;
        int c = 0;
        if (hasC && !Coercion.TryToInt(a[2].Evaluate(), out c, out var cerr)) return cerr;
        if (r == 0 || (hasC && c == 0)) return Err(ErrorCodes.Value);
        if (!hasC)
        {
            if (range.Rows == 1) { c = r; r = 1; }
            else if (range.Cols == 1) c = 1;
            else return Err(ErrorCodes.Value);
        }
        if (r < 1 || r > range.Rows || c < 1 || c > range.Cols) return Err(ErrorCodes.Ref);
        return range.Get(r - 1, c - 1);
    }

    private static CellValue Match(IReadOnlyList<Arg> a)
    {
        var v = ArgGuard.Required(a[0]);
        if (v.Kind == ValueKind.Error) return v;
        if (AsRange(a[1], out var err) is not { } range) return err;
        int type = 1;
        if (a.Count > 2 && !a[2].IsOmitted && !Coercion.TryToInt(a[2].Evaluate(), out type, out var terr)) return terr;
        if (range.Rows != 1 && range.Cols != 1) return Err(ErrorCodes.NA);
        int n = range.CellCount;
        CellValue At(int i) => range.Rows == 1 ? range.Get(0, i) : range.Get(i, 0);
        int hit;
        if (type == 0)
        {
            hit = -1;
            for (int i = 0; i < n; i++)
                if (ExactEqual(v, At(i))) { hit = i; break; }
        }
        else if (type > 0) hit = LastWhere(n, i => KeyLe(At(i), v));
        else hit = LastWhere(n, i => KeyGe(At(i), v));
        return hit < 0 ? Err(ErrorCodes.NA) : CellValue.FromNumber(hit + 1);
    }

    private static CellValue Choose(IReadOnlyList<Arg> a)
    {
        if (!Coercion.TryToInt(ArgGuard.Required(a[0]), out int i, out var err)) return err;
        if (i < 1 || i > a.Count - 1) return Err(ErrorCodes.Value);
        return a[i].Evaluate(); // only the chosen value is evaluated; a range in scalar context is #VALUE!
    }

    private static CellValue RowCol(IReadOnlyList<Arg> a, IEvalContext ctx, bool row)
    {
        if (a.Count == 0 || a[0].IsOmitted) return CellValue.FromNumber(row ? ctx.FormulaRow : ctx.FormulaCol);
        if (AsRange(a[0], out var err) is not { } range) return err;
        return CellValue.FromNumber(row ? range.Address.TopLeft.Row : range.Address.TopLeft.Col);
    }

    private static CellValue Dimension(Arg a, bool rows)
    {
        if (a.Range is { } r) return CellValue.FromNumber(rows ? r.Rows : r.Cols);
        var v = ArgGuard.Required(a);
        return v.Kind == ValueKind.Error ? v : CellValue.FromNumber(1); // a scalar is 1x1
    }
}
