using System.Globalization;
using System.Text;

namespace Gridline.Core.Functions;

internal enum RoundMode { HalfAwayFromZero, Up, Down }

/// <summary>Decimal rounding on the shortest round-trip representation of a double (§7.1).</summary>
internal static class Rounding
{
    public static double Round(double n, int digits, RoundMode mode)
    {
        if (n == 0 || !double.IsFinite(n)) return n;

        string s = n.ToString("R", CultureInfo.InvariantCulture);
        bool negative = s[0] == '-';
        if (negative) s = s[1..];

        // Split into mantissa digits D and exponent E with |n| = 0.D × 10^E.
        long exp = 0;
        int ePos = s.IndexOfAny(['E', 'e']);
        if (ePos >= 0)
        {
            exp = long.Parse(s[(ePos + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            s = s[..ePos];
        }
        int dot = s.IndexOf('.');
        string intPart = dot < 0 ? s : s[..dot];
        string fracPart = dot < 0 ? "" : s[(dot + 1)..];
        string all = intPart + fracPart;
        long e = intPart.Length + exp;
        int lead = 0;
        while (lead < all.Length - 1 && all[lead] == '0') lead++;
        e -= lead;
        string d = all[lead..].TrimEnd('0');
        if (d.Length == 0) return 0;

        long cut = e + digits;
        if (cut >= d.Length) return n;

        char[] kept;
        bool increment;
        if (cut < 0)
        {
            // Every digit is dropped, and the first one lies below the rounding position: only Up can reach a non-zero result.
            if (mode != RoundMode.Up) return 0;
            kept = [];
            increment = true;
        }
        else
        {
            kept = d.AsSpan(0, (int)cut).ToArray();
            increment = mode switch
            {
                RoundMode.HalfAwayFromZero => d[(int)cut] >= '5',
                RoundMode.Up => d.AsSpan((int)cut).ContainsAnyExcept('0'),
                _ => false,
            };
        }

        string resultDigits;
        long resultExp;
        if (!increment)
        {
            if (kept.Length == 0) return 0;
            resultDigits = new string(kept);
            resultExp = e;
        }
        else if (cut < 0)
        {
            resultDigits = "1";
            resultExp = 1L - digits;
        }
        else
        {
            int i = kept.Length - 1;
            while (i >= 0 && kept[i] == '9') { kept[i] = '0'; i--; }
            if (i >= 0) { kept[i]++; resultDigits = new string(kept); resultExp = e; }
            else { resultDigits = "1" + new string(kept); resultExp = e + 1; }
        }

        var sb = new StringBuilder("0.").Append(resultDigits).Append('E').Append(resultExp.ToString(CultureInfo.InvariantCulture));
        double r = double.Parse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return negative ? -r : r;
    }
}
