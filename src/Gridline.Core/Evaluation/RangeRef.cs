using Gridline.Core.Addressing;

namespace Gridline.Core.Evaluation;

/// <summary>A normalized rectangular reference on one sheet (an internal sheet id), handed to functions (§6.5, CR1.2).</summary>
internal sealed class RangeRef(int Sheet, RangeAddress Address, IEvalContext Ctx)
{
    public int Sheet { get; } = Sheet;
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
        return Ctx.GetCellValue(Sheet, Address.TopLeft.Row + r, Address.TopLeft.Col + c);
    }

    /// <summary>Non-blank cells of the range with their absolute row and column, row-major.</summary>
    public IEnumerable<(int Row, int Col, CellValue Value)> NonBlankCells() => Ctx.EnumerateNonBlank(Sheet, Address);

    /// <summary>Values of non-blank cells, row-major.</summary>
    public IEnumerable<CellValue> NonBlankRowMajor()
    {
        foreach (var (_, _, v) in Ctx.EnumerateNonBlank(Sheet, Address)) yield return v;
    }
}
