using Gridline.Core.Addressing;

namespace Gridline.Core.Evaluation;

/// <summary>
/// What the evaluator and functions may read from the workbook while evaluating one formula. Sheets are internal
/// sheet ids (CR1.1); every read names its sheet explicitly.
/// </summary>
internal interface IEvalContext
{
    /// <summary>The value of a cell on sheet <paramref name="sheet"/>; <see cref="CellValue.Blank"/> for an empty cell or unknown sheet.</summary>
    CellValue GetCellValue(int sheet, int row, int col);

    /// <summary>Resolves a sheet name as written in a formula (case-insensitive, CR1.2). False when there is no such sheet.</summary>
    bool TryResolveSheet(string name, out int sheet);

    /// <summary>
    /// Resolves a defined name (case-insensitive, §9.6, CR1.4) to its sheet and target. Returns false when the name
    /// is undefined (or its target is <c>#REF!</c>). <paramref name="isRange"/> is true when the name is bound to a
    /// range rather than a single cell.
    /// </summary>
    bool TryResolveName(string name, out int sheet, out RangeAddress target, out bool isRange);

    /// <summary>
    /// The value of a name that <see cref="TryResolveName"/> did not resolve: <c>#NAME?</c> when it is undefined,
    /// <c>#REF!</c> when it is defined with <c>refersTo</c> = <c>#REF!</c> (§9.6).
    /// </summary>
    CellValue UnresolvedNameValue(string name) => CellValue.FromError(ErrorCodes.Name);

    /// <summary>Non-blank cells of <paramref name="range"/> on <paramref name="sheet"/> in row-major order, in O(non-blank in range + log n).</summary>
    IEnumerable<(int Row, int Col, CellValue Value)> EnumerateNonBlank(int sheet, RangeAddress range);

    /// <summary>Sheet id of the cell whose formula is being evaluated; unqualified references use it (CR1.2).</summary>
    int FormulaSheet { get; }

    /// <summary>Row of the cell whose formula is being evaluated.</summary>
    int FormulaRow { get; }

    /// <summary>Column of the cell whose formula is being evaluated.</summary>
    int FormulaCol { get; }
}
