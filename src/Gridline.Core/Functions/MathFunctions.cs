using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>The 20 Math functions (§7.1).</summary>
internal static class MathFunctions
{
    public static void Register(FunctionRegistry r)
    {
        Add(r, "SUM", 1, 255, a => Aggregate(a, x => x.Sum()));
        Add(r, "AVERAGE", 1, 255, a => Aggregate(a, x => x.Sum() / x.Count, ErrorCodes.Div0));
        Add(r, "MIN", 1, 255, a => Aggregate(a, x => x.Min()));
        Add(r, "MAX", 1, 255, a => Aggregate(a, x => x.Max()));
        Add(r, "PRODUCT", 1, 255, a => Aggregate(a, Product));
        Add(r, "SUMSQ", 1, 255, a => Aggregate(a, x => x.Sum(v => v * v)));
        Add(r, "COUNT", 1, 255, Count);
        Add(r, "COUNTA", 1, 255, CountA);
        Add(r, "COUNTBLANK", 1, 1, a => CountBlank(a[0]));
        Add(r, "SUMPRODUCT", 1, 255, SumProduct);

        Unary(r, "ABS", Math.Abs);
        Unary(r, "SIGN", x => Math.Sign(x));
        Unary(r, "INT", Math.Floor);
        Add(r, "SQRT", 1, 1, a =>
            WithNumber(a[0], x => x < 0 ? CellValue.FromError(ErrorCodes.Num) : CellValue.FromNumber(Math.Sqrt(x))));
        Add(r, "MOD", 2, 2, a =>
            WithNumber(a[0], n => WithNumber(a[1], d =>
                d == 0 ? CellValue.FromError(ErrorCodes.Div0) : CellValue.FromNumber(n - d * Math.Floor(n / d)))));
        Add(r, "POWER", 2, 2, a =>
            WithNumber(a[0], b => WithNumber(a[1], e => Evaluator.Power(b, e))));
        Add(r, "PI", 0, 0, _ => CellValue.FromNumber(Math.PI));

        Rounder(r, "ROUND", RoundMode.HalfAwayFromZero);
        Rounder(r, "ROUNDUP", RoundMode.Up);
        Rounder(r, "ROUNDDOWN", RoundMode.Down);
    }

    private static void Add(FunctionRegistry r, string name, int min, int max, Func<IReadOnlyList<Arg>, CellValue> f) =>
        r.Register(name, min, max, new DelegateFunction((a, _) => f(a)));

    private static void Unary(FunctionRegistry r, string name, Func<double, double> f) =>
        Add(r, name, 1, 1, a => WithNumber(a[0], x => CellValue.FromNumber(f(x))));

    private static void Rounder(FunctionRegistry r, string name, RoundMode mode) =>
        Add(r, name, 2, 2, a =>
            WithNumber(a[0], n =>
            {
                if (!Coercion.TryToInt(ArgGuard.Required(a[1]), out int digits, out var err)) return err;
                return CellValue.FromNumber(Rounding.Round(n, digits, mode));
            }));

    private static CellValue WithNumber(Arg a, Func<double, CellValue> f)
    {
        var n = Coercion.ToNumber(ArgGuard.Required(a));
        return n.Kind == ValueKind.Error ? n : f(n.Number);
    }

    private static double Product(List<double> x)
    {
        double p = 1;
        foreach (var v in x) p *= v;
        return p;
    }

    private static CellValue Aggregate(IReadOnlyList<Arg> args, Func<List<double>, double> f, string? emptyError = null)
    {
        var nums = new List<double>();
        var err = Aggregation.Collect(args, nums);
        if (err.Kind == ValueKind.Error) return err;
        if (nums.Count == 0)
            return emptyError is not null ? CellValue.FromError(emptyError) : CellValue.FromNumber(0);
        return CellValue.FromNumber(f(nums));
    }

    private static CellValue Count(IReadOnlyList<Arg> args)
    {
        int count = 0;
        foreach (var a in args)
        {
            if (a.Range is { } range)
            {
                foreach (var v in range.NonBlankRowMajor())
                    if (v.Kind == ValueKind.Number) count++;
            }
            else
            {
                var v = a.Evaluate();
                if (v.Kind is ValueKind.Number or ValueKind.Boolean) count++;
                else if (v.Kind == ValueKind.Text && Coercion.ToNumber(v).Kind == ValueKind.Number) count++;
            }
        }
        return CellValue.FromNumber(count);
    }

    private static CellValue CountA(IReadOnlyList<Arg> args)
    {
        double count = 0;
        foreach (var a in args)
        {
            if (a.Range is { } range) count += range.NonBlankRowMajor().Count();
            else if (a.Evaluate().Kind != ValueKind.Blank) count++;
        }
        return CellValue.FromNumber(count);
    }

    private static CellValue CountBlank(Arg a)
    {
        if (a.Range is not { } range) return CellValue.FromError(ErrorCodes.Value);
        long nonBlank = 0, emptyText = 0;
        foreach (var v in range.NonBlankRowMajor())
        {
            nonBlank++;
            if (v.Kind == ValueKind.Text && v.Text.Length == 0) emptyText++;
        }
        return CellValue.FromNumber(range.CellCount - nonBlank + emptyText);
    }

    private static CellValue SumProduct(IReadOnlyList<Arg> args)
    {
        // Each argument becomes a sparse map of relative position -> number; non-numbers are absent (= 0).
        int rows = -1, cols = -1;
        var maps = new List<Dictionary<(int, int), double>>(args.Count);
        CellValue firstError = default;
        foreach (var a in args)
        {
            if (a.IsOmitted) continue; // variadic position: an omitted argument is absent
            var map = new Dictionary<(int, int), double>();
            int r, c;
            if (a.Range is { } range)
            {
                r = range.Rows; c = range.Cols;
                var top = range.Address.TopLeft;
                foreach (var (row, col, v) in range.Ctx.EnumerateNonBlank(range.Address))
                {
                    if (v.Kind == ValueKind.Error) { if (firstError.Kind != ValueKind.Error) firstError = v; }
                    else if (v.Kind == ValueKind.Number) map[(row - top.Row, col - top.Col)] = v.Number;
                }
            }
            else
            {
                r = c = 1;
                var v = a.Evaluate();
                if (v.Kind == ValueKind.Error) { if (firstError.Kind != ValueKind.Error) firstError = v; }
                else if (v.Kind == ValueKind.Number) map[(0, 0)] = v.Number;
            }
            if (rows < 0) { rows = r; cols = c; }
            else if (rows != r || cols != c) return CellValue.FromError(ErrorCodes.Value);
            maps.Add(map);
        }
        if (firstError.Kind == ValueKind.Error) return firstError;
        if (maps.Count == 0) return CellValue.FromNumber(0);

        var smallest = maps[0];
        foreach (var m in maps) if (m.Count < smallest.Count) smallest = m;
        double sum = 0;
        foreach (var pos in smallest.Keys)
        {
            double p = 1;
            foreach (var m in maps)
            {
                if (!m.TryGetValue(pos, out double x)) { p = 0; break; }
                p *= x;
            }
            sum += p;
        }
        return CellValue.FromNumber(sum);
    }
}
