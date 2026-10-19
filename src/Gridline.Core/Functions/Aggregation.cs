using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>A running fold over the numbers of an aggregate's arguments, in argument then row-major order.</summary>
internal interface IAccumulator
{
    void Add(double v);
}

/// <summary>The §7.0 "agg" argument rules shared by SUM, AVERAGE, MIN, MAX, PRODUCT and SUMSQ.</summary>
internal static class Aggregation
{
    /// <summary>
    /// Feeds the numbers of <paramref name="args"/> to <paramref name="acc"/> in order and returns how many there were.
    /// Reference args contribute numbers only (text, booleans and blanks are ignored); direct args are coerced
    /// (omitted counts as 0). <paramref name="error"/> is the first error encountered, or Blank (<c>default</c>).
    /// Streaming instead of collecting into a list keeps a 100k-cell SUM allocation-free (§12 P-7).
    /// </summary>
    public static int Collect<T>(IReadOnlyList<Arg> args, ref T acc, out CellValue error) where T : struct, IAccumulator
    {
        int count = 0;
        error = default;
        foreach (var a in args)
        {
            if (a.Range is { } range)
            {
                foreach (var v in range.NonBlankRowMajor())
                {
                    if (v.Kind == ValueKind.Error) { error = v; return count; }
                    if (v.Kind == ValueKind.Number) { acc.Add(v.Number); count++; }
                }
            }
            else
            {
                if (a.IsOmitted) continue; // variadic position: an omitted argument is absent
                var n = Coercion.ToNumber(a.Evaluate());
                if (n.Kind == ValueKind.Error) { error = n; return count; }
                acc.Add(n.Number);
                count++;
            }
        }
        return count;
    }
}
