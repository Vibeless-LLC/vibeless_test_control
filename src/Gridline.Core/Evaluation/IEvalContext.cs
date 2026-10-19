using Gridline.Core.Addressing;

namespace Gridline.Core.Evaluation;

/// <summary>What the evaluator and functions may read from the workbook while evaluating one formula.</summary>
internal interface IEvalContext
{
    /// <summary>The cell's current value; <see cref="CellValue.Blank"/> for an empty cell.</summary>
    CellValue GetCellValue(int row, int col);

    /// <summary>
    /// Resolves a defined name (case-insensitive, §9.6). Returns false when the name is undefined.
    /// <paramref name="isRange"/> is true when the name is bound to a range rather than a single cell.
    /// </summary>
    bool TryResolveName(string name, out RangeAddress target, out bool isRange);

    /// <summary>Non-blank cells of <paramref name="range"/> in row-major order, in O(non-blank in range + log n).</summary>
    IEnumerable<(int Row, int Col, CellValue Value)> EnumerateNonBlank(RangeAddress range);

    /// <summary>Row of the cell whose formula is being evaluated.</summary>
    int FormulaRow { get; }

    /// <summary>Column of the cell whose formula is being evaluated.</summary>
    int FormulaCol { get; }
}
