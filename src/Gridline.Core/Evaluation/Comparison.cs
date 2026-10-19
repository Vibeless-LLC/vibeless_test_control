namespace Gridline.Core.Evaluation;

/// <summary>Total ordering used by the comparison operators (§6.3).</summary>
internal static class Comparison
{
    /// <summary>
    /// Returns -1, 0 or 1. A Blank operand adapts to the other's kind (0, "" or FALSE); Blank equals Blank.
    /// Kinds order Number &lt; Text &lt; Boolean. Text compares ordinally on <c>ToUpperInvariant()</c>; numbers exactly.
    /// </summary>
    /// <exception cref="ArgumentException">Either argument is an Error.</exception>
    public static int Compare(CellValue a, CellValue b)
    {
        if (a.Kind == ValueKind.Error || b.Kind == ValueKind.Error)
            throw new ArgumentException("Comparison operands must not be errors.");

        if (a.Kind == ValueKind.Blank && b.Kind == ValueKind.Blank) return 0;
        if (a.Kind == ValueKind.Blank) a = DefaultOf(b.Kind);
        else if (b.Kind == ValueKind.Blank) b = DefaultOf(a.Kind);

        int ra = Rank(a.Kind), rb = Rank(b.Kind);
        if (ra != rb) return ra < rb ? -1 : 1;

        return a.Kind switch
        {
            ValueKind.Number => a.Number < b.Number ? -1 : a.Number > b.Number ? 1 : 0,
            ValueKind.Text => Math.Sign(string.CompareOrdinal(a.Text.ToUpperInvariant(), b.Text.ToUpperInvariant())),
            _ => a.Boolean == b.Boolean ? 0 : a.Boolean ? 1 : -1,
        };
    }

    private static CellValue DefaultOf(ValueKind k) => k switch
    {
        ValueKind.Number => CellValue.FromNumber(0),
        ValueKind.Text => CellValue.FromText(""),
        _ => CellValue.FromBoolean(false),
    };

    private static int Rank(ValueKind k) => k switch
    {
        ValueKind.Number => 0,
        ValueKind.Text => 1,
        _ => 2,
    };
}
