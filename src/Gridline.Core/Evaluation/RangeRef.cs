using Gridline.Core.Addressing;

namespace Gridline.Core.Evaluation;

/// <summary>A normalized rectangular reference handed to functions (§6.5).</summary>
internal sealed class RangeRef(RangeAddress Address, IEvalContext Ctx)
{
    public RangeAddress Address { get; } = Address;
    public IEvalContext Ctx { get; } = Ctx;

    public int Rows => Address.Rows;
    public int Cols => Address.Cols;
    public int CellCount => Rows * Cols;

    /// <summary>The value at 0-based (<paramref name="r"/>, <paramref name="c"/>) within the range; Blank if empty.</summary>
    public CellValue Get(int r, int c)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(r);
        ArgumentOutOfRangeException.ThrowIfNegative(c);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(r, Rows);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(c, Cols);
        return Ctx.GetCellValue(Address.TopLeft.Row + r, Address.TopLeft.Col + c);
    }

    /// <summary>Values of non-blank cells, row-major.</summary>
    public IEnumerable<CellValue> NonBlankRowMajor()
    {
        foreach (var (_, _, v) in Ctx.EnumerateNonBlank(Address)) yield return v;
    }
}
