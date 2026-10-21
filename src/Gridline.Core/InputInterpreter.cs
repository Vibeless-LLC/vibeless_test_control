using Gridline.Core.Evaluation;
using Gridline.Core.Parsing;

namespace Gridline.Core;

/// <summary>Applies the §4.3 cell input rules, in order; the first match wins.</summary>
internal static class InputInterpreter
{
    /// <summary>
    /// The cell for <paramref name="input"/>, or null for <c>""</c> (blank, rule 1). A formula's value is left
    /// Blank until recalculation evaluates it. Never throws for a string within the input limit.
    /// </summary>
    public static Cell? Interpret(string input, IFunctionArity arity)
    {
        if (input.Length == 0) return null;                                    // 1. blank

        if (input[0] == '=') return FromFormula(input, Parser.Parse(input[1..], arity)); // 2. formula

        if (input[0] == '\'')                                                  // 3. text after apostrophe
            return new Cell(input, CellKind.Text, null, CellValue.FromText(input[1..]), null);

        if (input.Equals("TRUE", StringComparison.OrdinalIgnoreCase))          // 4. boolean
            return new Cell(input, CellKind.Boolean, null, CellValue.FromBoolean(true), null);
        if (input.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return new Cell(input, CellKind.Boolean, null, CellValue.FromBoolean(false), null);

        if (NumberGrammar.TryParse(input, out double d))                       // 5. number (finite only)
            return new Cell(input, CellKind.Number, null, CellValue.FromNumber(d), null);

        return new Cell(input, CellKind.Text, null, CellValue.FromText(input), null); // 6. text verbatim
    }

    /// <summary>
    /// The formula cell for <paramref name="input"/> whose parse is the (error-free) <paramref name="root"/>: used
    /// when <paramref name="input"/> was printed from <paramref name="root"/>, so re-parsing it is skipped (CR1.9, P-8).
    /// </summary>
    public static Cell FromFormula(string input, Node root) => FromFormula(input, new ParsedFormula(root, IsParseError: false));

    private static Cell FromFormula(string input, ParsedFormula parsed) =>
        new(input, CellKind.Formula, parsed, CellValue.Blank, DependencyExtractor.Extract(parsed));
}
