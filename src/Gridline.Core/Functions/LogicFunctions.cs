using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>The 13 logic and information functions (§7.2).</summary>
internal static class LogicFunctions
{
    public static void Register(FunctionRegistry r)
    {
        Add(r, "IF", 2, 3, If);
        Add(r, "AND", 1, 255, a => Logical(a, (t, f) => f == 0));
        Add(r, "OR", 1, 255, a => Logical(a, (t, f) => t > 0));
        Add(r, "XOR", 1, 255, a => Logical(a, (t, f) => (t & 1) == 1));
        Add(r, "NOT", 1, 1, a =>
        {
            var b = Coercion.ToBoolean(ArgGuard.Required(a[0]));
            return b.Kind == ValueKind.Error ? b : CellValue.FromBoolean(!b.Boolean);
        });
        Add(r, "IFERROR", 2, 2, a =>
        {
            var v = ArgGuard.Required(a[0]);
            return v.Kind == ValueKind.Error && !a[0].IsOmitted ? ArgGuard.Required(a[1]) : v;
        });
        Add(r, "IFNA", 2, 2, a =>
        {
            var v = ArgGuard.Required(a[0]);
            return v.Kind == ValueKind.Error && v.Error == ErrorCodes.NA && !a[0].IsOmitted ? ArgGuard.Required(a[1]) : v;
        });
        Add(r, "ISBLANK", 1, 1, a =>
            CellValue.FromBoolean(a[0].IsReference && !a[0].IsRange && a[0].Evaluate().Kind == ValueKind.Blank));
        Is(r, "ISNUMBER", v => v.Kind == ValueKind.Number);
        Is(r, "ISTEXT", v => v.Kind == ValueKind.Text);
        Is(r, "ISLOGICAL", v => v.Kind == ValueKind.Boolean);
        Is(r, "ISERROR", v => v.Kind == ValueKind.Error);
        Is(r, "ISNA", v => v.Kind == ValueKind.Error && v.Error == ErrorCodes.NA);
    }

    private static void Add(FunctionRegistry r, string name, int min, int max, Func<IReadOnlyList<Arg>, CellValue> f) =>
        r.Register(name, min, max, new DelegateFunction((a, _) => f(a)));

    private static void Is(FunctionRegistry r, string name, Func<CellValue, bool> test) =>
        Add(r, name, 1, 1, a => a[0].IsOmitted ? CellValue.FromError(ErrorCodes.Value) : CellValue.FromBoolean(test(a[0].Evaluate())));

    private static CellValue If(IReadOnlyList<Arg> a)
    {
        var c = Coercion.ToBoolean(ArgGuard.Required(a[0]));
        if (c.Kind == ValueKind.Error) return c;
        if (c.Boolean)
            return a[1].IsOmitted ? CellValue.FromNumber(0) : a[1].Evaluate();
        if (a.Count < 3 || a[2].IsOmitted) return CellValue.FromBoolean(false);
        return a[2].Evaluate();
    }

    /// <summary>Shared AND/OR/XOR scan; <paramref name="decide"/> gets the (true, false) counts.</summary>
    private static CellValue Logical(IReadOnlyList<Arg> args, Func<long, long, bool> decide)
    {
        long trues = 0, falses = 0, found = 0;
        foreach (var a in args)
        {
            if (a.Range is { } range)
            {
                foreach (var v in range.NonBlankRowMajor())
                {
                    if (v.Kind == ValueKind.Error) return v;
                    if (v.Kind is ValueKind.Boolean or ValueKind.Number)
                    {
                        found++;
                        if (Coercion.ToBoolean(v).Boolean) trues++; else falses++;
                    }
                }
            }
            else
            {
                if (a.IsOmitted) continue; // variadic position: an omitted argument is absent
                var b = Coercion.ToBoolean(a.Evaluate());
                if (b.Kind == ValueKind.Error) return b;
                found++;
                if (b.Boolean) trues++; else falses++;
            }
        }
        if (found == 0) return CellValue.FromError(ErrorCodes.Value);
        return CellValue.FromBoolean(decide(trues, falses));
    }
}
