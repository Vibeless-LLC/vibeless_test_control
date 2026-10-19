using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>The §7.0 "agg" argument rules shared by SUM, AVERAGE, MIN, MAX, PRODUCT and SUMSQ.</summary>
internal static class Aggregation
{
    /// <summary>
    /// Appends the numbers of <paramref name="args"/> to <paramref name="into"/>. Reference args contribute numbers only
    /// (text, booleans and blanks are ignored); direct args are coerced (omitted counts as 0). Returns the first error
    /// encountered, or Blank (<c>default</c>) on success.
    /// </summary>
    public static CellValue Collect(IReadOnlyList<Arg> args, List<double> into)
    {
        foreach (var a in args)
        {
            if (a.Range is { } range)
            {
                foreach (var v in range.NonBlankRowMajor())
                {
                    if (v.Kind == ValueKind.Error) return v;
                    if (v.Kind == ValueKind.Number) into.Add(v.Number);
                }
            }
            else
            {
                if (a.IsOmitted) continue; // variadic position: an omitted argument is absent
                var n = Coercion.ToNumber(a.Evaluate());
                if (n.Kind == ValueKind.Error) return n;
                into.Add(n.Number);
            }
        }
        return default;
    }
}
