namespace Gridline.Core.Tests;

/// <summary>
/// Evaluates a formula against a few cell inputs through the real <see cref="Workbook"/> and returns the
/// canonical value string. The formula is placed in Z100, which the tests' inputs and ranges do not use.
/// </summary>
internal static class CoreTestHost
{
    private const string FormulaCell = "Z100";

    public static string Eval(string formula, params (string addr, string input)[] inputs)
    {
        var wb = new Workbook();
        foreach (var (addr, input) in inputs) wb.SetCell(addr, input);
        wb.SetCell(FormulaCell, formula);
        return wb.GetValue(FormulaCell).ToCanonicalString();
    }
}
