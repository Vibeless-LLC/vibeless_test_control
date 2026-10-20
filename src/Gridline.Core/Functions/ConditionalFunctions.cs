using Gridline.Core.Addressing;
using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>SUMIF, COUNTIF and AVERAGEIF (§7.5).</summary>
internal static class ConditionalFunctions
{
    public static void Register(FunctionRegistry r)
    {
        r.Register("SUMIF", 2, 3, new DelegateFunction((a, _) => SumOrAverage(a, average: false)));
        r.Register("AVERAGEIF", 2, 3, new DelegateFunction((a, _) => SumOrAverage(a, average: true)));
        r.Register("COUNTIF", 2, 2, new DelegateFunction((a, _) => CountIf(a)));
    }

    /// <summary>Validates range and criteria (shared by all three); returns the error to produce, or default when fine.</summary>
    private static CellValue Prepare(IReadOnlyList<Arg> a, out RangeRef range, out Criteria criteria)
    {
        range = null!;
        criteria = null!;
        if (a[0].Range is not { } r)
        {
            var v = a[0].Evaluate();
            return v.Kind == ValueKind.Error ? v : CellValue.FromError(ErrorCodes.Value);
        }
        var c = ArgGuard.Required(a[1]);
        if (c.Kind == ValueKind.Error) return c;
        range = r;
        criteria = Criteria.FromValue(c);
        return default;
    }

    private static CellValue CountIf(IReadOnlyList<Arg> a)
    {
        var err = Prepare(a, out var range, out var crit);
        if (err.Kind == ValueKind.Error) return err;
        long count = 0, nonBlank = 0;
        foreach (var v in range.NonBlankRowMajor())
        {
            nonBlank++;
            if (crit.Matches(v)) count++;
        }
        if (crit.MatchesBlank) count += (long)range.CellCount - nonBlank; // blank positions are never visited
        return CellValue.FromNumber(count);
    }

    private static CellValue SumOrAverage(IReadOnlyList<Arg> a, bool average)
    {
        var err = Prepare(a, out var range, out var crit);
        if (err.Kind == ValueKind.Error) return err;

        // sum_range is anchored at its top-left and resized to range's dimensions.
        RangeRef sumRange = range;
        if (a.Count > 2 && !a[2].IsOmitted)
        {
            if (a[2].Range is not { } sr)
            {
                var v = a[2].Evaluate();
                return v.Kind == ValueKind.Error ? v : CellValue.FromError(ErrorCodes.Value);
            }
            sumRange = sr;
        }
        var ctx = range.Ctx;
        var rtl = range.Address.TopLeft;
        var stl = sumRange.Address.TopLeft;

        double sum = 0;
        long n = 0;

        // Returns the error to propagate, if the sum cell is one.
        bool Add(CellValue sv, out CellValue error)
        {
            error = default;
            if (sv.Kind == ValueKind.Error) { error = sv; return false; }
            if (sv.Kind == ValueKind.Number) { sum += sv.Number; n++; }
            return true;
        }

        if (!crit.MatchesBlank)
        {
            // Only non-blank range cells can match: walk them and read the paired sum cell.
            foreach (var (row, col, v) in ctx.EnumerateNonBlank(range.Address))
            {
                if (!crit.Matches(v)) continue;
                int sRow = stl.Row + (row - rtl.Row), sCol = stl.Col + (col - rtl.Col);
                var sv = sRow <= CellAddress.MaxRows && sCol <= CellAddress.MaxColumns ? ctx.GetCellValue(sRow, sCol) : default;
                if (!Add(sv, out var e)) return e;
            }
        }
        else
        {
            // Blank range cells match too, but a blank sum cell contributes nothing: walk the non-blank sum cells
            // (clipped to the grid) and test the paired range cell.
            int bottom = Math.Min(CellAddress.MaxRows, stl.Row + range.Rows - 1);
            int right = Math.Min(CellAddress.MaxColumns, stl.Col + range.Cols - 1);
            var region = new RangeAddress(stl, new CellAddress(bottom, right));
            foreach (var (row, col, sv) in ctx.EnumerateNonBlank(region))
            {
                var rv = ctx.GetCellValue(rtl.Row + (row - stl.Row), rtl.Col + (col - stl.Col));
                if (!crit.Matches(rv)) continue;
                if (!Add(sv, out var e)) return e;
            }
        }

        if (!average) return CellValue.FromNumber(sum);
        return n == 0 ? CellValue.FromError(ErrorCodes.Div0) : CellValue.FromNumber(sum / n);
    }
}
