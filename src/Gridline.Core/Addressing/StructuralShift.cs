namespace Gridline.Core.Addressing;

/// <summary>The axis of a structural edit (§9.5).</summary>
public enum Axis { Row, Column }

/// <summary>
/// Insert (<paramref name="IsInsert"/>) or delete <paramref name="Count"/> rows or columns at <paramref name="At"/>
/// (1-based), on <paramref name="Axis"/> (§9.5).
/// </summary>
public readonly record struct StructuralEdit(Axis Axis, int At, int Count, bool IsInsert);

/// <summary>Index and span adjustment under insert/delete of rows or columns (§9.5). Overflow-safe for any count.</summary>
public static class StructuralShift
{
    /// <summary>
    /// Insert: <c>x ≥ At</c> moves to <c>x + Count</c>; fails when that exceeds <paramref name="max"/>.
    /// Delete: <c>x</c> inside <c>[At, At+Count-1]</c> fails; <c>x</c> below the span moves to <c>x − Count</c>.
    /// </summary>
    public static bool TryShiftIndex(int x, StructuralEdit e, int max, out int result)
    {
        result = x;
        if (x < e.At) return true;
        long n = e.Count;
        if (e.IsInsert)
        {
            long r = x + n;
            if (r > max) return false;
            result = (int)r;
            return true;
        }
        if (x <= e.At + n - 1) return false;
        result = (int)(x - n);
        return true;
    }

    /// <summary>
    /// Shifts the span <c>[lo, hi]</c> (<c>lo ≤ hi</c>). Insert: each endpoint independently; fails when the new
    /// low end exceeds <paramref name="max"/>, and a new high end past <paramref name="max"/> is clamped to it.
    /// Delete: the surviving indices are kept; fails when none survive, otherwise the new endpoints are the first
    /// and last survivors after the shift.
    /// </summary>
    public static bool TryShiftSpan(int lo, int hi, StructuralEdit e, int max, out int newLo, out int newHi)
    {
        newLo = lo;
        newHi = hi;
        long n = e.Count;
        if (e.IsInsert)
        {
            long l = lo >= e.At ? lo + n : lo;
            long h = hi >= e.At ? hi + n : hi;
            if (l > max) return false;
            newLo = (int)l;
            newHi = (int)Math.Min(h, max);
            return true;
        }
        long delLo = e.At, delHi = e.At + n - 1;
        if (lo >= delLo && hi <= delHi) return false; // nothing survives
        // An endpoint inside the deleted span moves to the nearest survivor inward; survivors below the span shift.
        long first = lo >= delLo && lo <= delHi ? delHi + 1 : lo;
        long last = hi >= delLo && hi <= delHi ? delLo - 1 : hi;
        newLo = (int)(first > delHi ? first - n : first);
        newHi = (int)(last > delHi ? last - n : last);
        return true;
    }
}
