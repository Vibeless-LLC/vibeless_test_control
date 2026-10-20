namespace Gridline.Core.Functions;

/// <summary>
/// The Excel 1900 date serial system (§7.6), including the fictitious 1900-02-29 (serial 60).
/// Serial 0 is "1900-01-00"; valid serials are 0..<see cref="Max"/> (9999-12-31).
/// </summary>
internal static class DateSerial
{
    public const int Max = 2958465;

    private static readonly int Epoch = new DateOnly(1899, 12, 30).DayNumber;
    private static readonly DateOnly Dec31 = new(1899, 12, 31);
    private static readonly DateOnly Dec30 = new(1899, 12, 30);

    /// <summary>
    /// Serial of year <paramref name="y"/>, month <paramref name="m"/>, day <paramref name="d"/>. Months outside 1..12
    /// roll the year (floor division) and days outside the month roll the month. False when the result is outside
    /// [0, <see cref="Max"/>] (this includes any month before 1900-01).
    /// </summary>
    public static bool TryFromYmd(long y, long m, long d, out int serial)
    {
        serial = 0;
        long months = y * 12 + (m - 1);
        long y2 = Math.DivRem(months, 12, out long rem);
        if (rem < 0) { rem += 12; y2--; }
        if (y2 < 1900 || y2 > 9999) return false;
        long s = FirstOfMonth((int)y2, (int)rem + 1) + d - 1;
        if (s < 0 || s > Max) return false;
        serial = (int)s;
        return true;
    }

    /// <summary>Serial of the first of the month, for years 1900..9999.</summary>
    private static int FirstOfMonth(int y, int m)
    {
        if (y == 1900 && m == 1) return 1;
        if (y == 1900 && m == 2) return 32;
        return new DateOnly(y, m, 1).DayNumber - Epoch;
    }

    /// <summary>Days in the month, treating 1900-02 as having 29 days (the fictitious leap day).</summary>
    public static int DaysInMonth(int y, int m) => y == 1900 && m == 2 ? 29 : DateTime.DaysInMonth(y, m);

    /// <summary>Year, month and day of a valid serial: 0 is (1900,1,0) and 60 is (1900,2,29).</summary>
    public static (int Y, int M, int D) ToYmd(int serial)
    {
        if (serial == 0) return (1900, 1, 0);
        if (serial == 60) return (1900, 2, 29);
        var date = serial < 60 ? Dec31.AddDays(serial) : Dec30.AddDays(serial);
        return (date.Year, date.Month, date.Day);
    }
}
