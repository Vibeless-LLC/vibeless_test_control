using Gridline.Core.Addressing;
using Gridline.Core.Evaluation;
using Gridline.Core.Functions;
using Gridline.Core.Parsing;

namespace Gridline.Core.Tests;

/// <summary>
/// Evaluates a formula against a few cell inputs and returns the canonical value string.
/// Until the Workbook exists (Task 10) this uses a small test-side interpreter of cell inputs; the signature is stable.
/// </summary>
internal static class CoreTestHost
{
    private sealed class DictContext : IEvalContext
    {
        public Dictionary<(int Row, int Col), CellValue> Cells { get; } = new();
        public int FormulaRow { get; set; } = 1;
        public int FormulaCol { get; set; } = 1;

        public CellValue GetCellValue(int row, int col) => Cells.TryGetValue((row, col), out var v) ? v : CellValue.Blank;

        public bool TryResolveName(string name, out RangeAddress target, out bool isRange)
        {
            target = default; isRange = false; return false;
        }

        public IEnumerable<(int Row, int Col, CellValue Value)> EnumerateNonBlank(RangeAddress range) =>
            Cells.Where(kv => kv.Value.Kind != ValueKind.Blank && range.Contains(new CellAddress(kv.Key.Row, kv.Key.Col)))
                 .OrderBy(kv => kv.Key.Row).ThenBy(kv => kv.Key.Col)
                 .Select(kv => (kv.Key.Row, kv.Key.Col, kv.Value));
    }

    public static string Eval(string formula, params (string addr, string input)[] inputs)
    {
        var registry = FunctionRegistry.CreateDefault();
        var arity = new FunctionArityAdapter(registry);
        var evaluator = new Evaluator(registry.Functions);
        var ctx = new DictContext();

        foreach (var (addr, input) in inputs)
        {
            if (!CellAddress.TryParse(addr, out var a)) throw new ArgumentException($"Bad address '{addr}'.");
            CellValue v;
            if (input.Length == 0) v = CellValue.Blank;
            else if (input[0] == '\'') v = CellValue.FromText(input[1..]);
            else if (input.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) v = CellValue.FromBoolean(true);
            else if (input.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) v = CellValue.FromBoolean(false);
            else if (NumberGrammar.TryParse(input, out double d)) v = CellValue.FromNumber(d);
            else if (input[0] == '=')
            {
                ctx.FormulaRow = a.Row; ctx.FormulaCol = a.Col;
                v = evaluator.Evaluate(Parser.Parse(input[1..], arity), ctx);
            }
            else v = CellValue.FromText(input);

            if (v.Kind == ValueKind.Blank) ctx.Cells.Remove((a.Row, a.Col));
            else ctx.Cells[(a.Row, a.Col)] = v;
        }

        ctx.FormulaRow = 1; ctx.FormulaCol = 1;
        return evaluator.Evaluate(Parser.Parse(formula[1..], arity), ctx).ToCanonicalString();
    }
}
