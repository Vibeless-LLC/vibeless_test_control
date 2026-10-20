using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>The 8 date functions (§7.6). Text is never parsed as a date; arguments are numbers truncated per §6.9.</summary>
internal static class DateFunctions
{
    public static void Register(FunctionRegistry r)
    {
        Add(r, "DATE", 3, 3, Date);
        Add(r, "YEAR", 1, 1, a => Part(a[0], t => t.Y));
        Add(r, "MONTH", 1, 1, a => Part(a[0], t => t.M));
        Add(r, "DAY", 1, 1, a => Part(a[0], t => t.D));
        Add(r, "WEEKDAY", 1, 2, Weekday);
        Add(r, "EDATE", 2, 2, a => Shift(a, endOfMonth: false));
        Add(r, "EOMONTH", 2, 2, a => Shift(a, endOfMonth: true));
        Add(r, "DAYS", 2, 2, Days);
    }

    private static void Add(FunctionRegistry r, string name, int min, int max, Func<IReadOnlyList<Arg>, CellValue> f) =>
        r.Register(name, min, max, new DelegateFunction((a, _) => f(a)));

    private static CellValue Num() => CellValue.FromError(ErrorCodes.Num);

    /// <summary>A valid serial (0..Max) from an argument, or the error to return.</summary>
    private static bool TrySerial(Arg a, out int serial, out CellValue error)
    {
        serial = 0;
        if (!Coercion.TryToInt(ArgGuard.Required(a), out int s, out error)) return false;
        if (s < 0 || s > DateSerial.Max) { error = Num(); return false; }
        serial = s;
        return true;
    }

    private static CellValue Date(IReadOnlyList<Arg> a)
    {
        if (!Coercion.TryToInt(ArgGuard.Required(a[0]), out int y, out var e)) return e;
        if (!Coercion.TryToInt(ArgGuard.Required(a[1]), out int m, out e)) return e;
        if (!Coercion.TryToInt(ArgGuard.Required(a[2]), out int d, out e)) return e;
        if (y < 0 || y >= 10000) return Num();
        if (y <= 1899) y += 1900;
        return DateSerial.TryFromYmd(y, m, d, out int serial) ? CellValue.FromNumber(serial) : Num();
    }

    private static CellValue Part(Arg a, Func<(int Y, int M, int D), int> pick)
    {
        if (!TrySerial(a, out int s, out var e)) return e;
        return CellValue.FromNumber(pick(DateSerial.ToYmd(s)));
    }

    private static CellValue Weekday(IReadOnlyList<Arg> a)
    {
        if (!TrySerial(a[0], out int s, out var e)) return e;
        int type = 1;
        if (a.Count > 1 && !a[1].IsOmitted && !Coercion.TryToInt(a[1].Evaluate(), out type, out e)) return e;
        switch (type)
        {
            case 1: return CellValue.FromNumber(FloorMod(s - 1, 7) + 1);   // Sunday = 1
            case 2: return CellValue.FromNumber(FloorMod(s - 2, 7) + 1);   // Monday = 1 .. Sunday = 7
            case 3: return CellValue.FromNumber(FloorMod(s - 2, 7));       // Monday = 0 .. Sunday = 6
            default: return Num();
        }
    }

    private static int FloorMod(int x, int m) => ((x % m) + m) % m;

    private static CellValue Shift(IReadOnlyList<Arg> a, bool endOfMonth)
    {
        if (!TrySerial(a[0], out int s, out var e)) return e;
        if (!Coercion.TryToInt(ArgGuard.Required(a[1]), out int months, out e)) return e;
        var (y, m, d) = DateSerial.ToYmd(s);
        long total = (long)y * 12 + (m - 1) + months;
        long ty = Math.DivRem(total, 12, out long rem);
        if (rem < 0) { rem += 12; ty--; }
        if (ty < 1900 || ty > 9999) return Num();
        int tm = (int)rem + 1;
        int last = DateSerial.DaysInMonth((int)ty, tm);
        int day = endOfMonth ? last : Math.Min(d, last);
        return DateSerial.TryFromYmd(ty, tm, day, out int serial) ? CellValue.FromNumber(serial) : Num();
    }

    private static CellValue Days(IReadOnlyList<Arg> a)
    {
        var end = Coercion.ToNumber(ArgGuard.Required(a[0]));
        if (end.Kind == ValueKind.Error) return end;
        var start = Coercion.ToNumber(ArgGuard.Required(a[1]));
        if (start.Kind == ValueKind.Error) return start;
        return CellValue.FromNumber(Math.Floor(end.Number) - Math.Floor(start.Number));
    }
}
