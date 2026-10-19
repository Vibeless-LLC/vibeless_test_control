using System.Globalization;

namespace Gridline.Core.Evaluation;

/// <summary>
/// The §4.3 rule 5 number grammar: <c>^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?%?$</c>, ASCII digits only,
/// invariant culture, no trimming. A trailing <c>%</c> divides by 100.
/// </summary>
internal static class NumberGrammar
{
    /// <summary>Returns false if <paramref name="s"/> does not match, or the result is not finite.</summary>
    public static bool TryParse(string s, out double value)
    {
        value = 0;
        int i = 0, n = s.Length;
        if (i < n && (s[i] == '+' || s[i] == '-')) i++;

        int intDigits = ScanDigits(s, ref i);
        int fracDigits = 0;
        if (i < n && s[i] == '.')
        {
            i++;
            fracDigits = ScanDigits(s, ref i);
        }
        if (intDigits == 0 && fracDigits == 0) return false; // "", "+", ".", "+."

        if (i < n && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < n && (s[i] == '+' || s[i] == '-')) i++;
            if (ScanDigits(s, ref i) == 0) return false;
        }

        int end = i;
        bool percent = false;
        if (i < n && s[i] == '%') { percent = true; i++; }
        if (i != n) return false;

        double v = double.Parse(s.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (percent) v /= 100;
        if (!double.IsFinite(v)) return false;
        value = v;
        return true;
    }

    private static int ScanDigits(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        return i - start;
    }
}
