using System.Collections.Frozen;

namespace Gridline.Core;

/// <summary>The error codes a cell value can carry (§4.5).</summary>
public static class ErrorCodes
{
    public const string Div0 = "#DIV/0!", Value = "#VALUE!", Ref = "#REF!", Name = "#NAME?",
                        NA = "#N/A", Num = "#NUM!", Circ = "#CIRC!", Parse = "#PARSE!";

    internal static readonly FrozenSet<string> All =
        new[] { Div0, Value, Ref, Name, NA, Num, Circ, Parse }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>True if <paramref name="code"/> is exactly one of the defined error codes.</summary>
    public static bool IsValid(string code) => code is not null && All.Contains(code);
}
