using Gridline.Core.Dependencies;
using Gridline.Core.Parsing;

namespace Gridline.Core;

/// <summary>How a non-blank cell's input was interpreted (§4.3). Blank cells are not stored.</summary>
internal enum CellKind { Number, Text, Boolean, Formula }

/// <summary>
/// One stored (non-blank) cell. <see cref="Input"/> is the exact string set by the user. For a formula,
/// <see cref="Formula"/> is its parse and <see cref="Deps"/> its static dependencies (null when it has none);
/// <see cref="Value"/> is updated in place by recalculation.
/// </summary>
internal sealed class Cell(string input, CellKind kind, ParsedFormula? formula, CellValue value, FormulaDeps? deps)
{
    public readonly string Input = input;
    public readonly CellKind Kind = kind;
    public readonly ParsedFormula? Formula = formula;
    public CellValue Value = value;
    public readonly FormulaDeps? Deps = deps;
}
