using System.Globalization;

namespace Gridline.Core.Evaluation;

/// <summary>Value coercions (§6.2). Each returns either the target kind or an Error.</summary>
internal static class Coercion
{
    /// <summary>Number or Error. Text is trimmed of U+0020 only, then parsed with <see cref="NumberGrammar"/>.</summary>
    public static CellValue ToNumber(CellValue v) => v.Kind switch
    {
        ValueKind.Number => v,
        ValueKind.Blank => CellValue.FromNumber(0),
        ValueKind.Boolean => CellValue.FromNumber(v.Boolean ? 1 : 0),
        ValueKind.Text => NumberGrammar.TryParse(v.Text.Trim(' '), out var d)
            ? CellValue.FromNumber(d)
            : CellValue.FromError(ErrorCodes.Value),
        _ => v,
    };

    /// <summary>Text or Error. Numbers use G15 invariant.</summary>
    public static CellValue ToText(CellValue v) => v.Kind switch
    {
        ValueKind.Text => v,
        ValueKind.Blank => CellValue.FromText(""),
        ValueKind.Boolean => CellValue.FromText(v.Boolean ? "TRUE" : "FALSE"),
        ValueKind.Number => CellValue.FromText(v.Number.ToString("G15", CultureInfo.InvariantCulture)),
        _ => v,
    };

    /// <summary>Boolean or Error. Text must be TRUE/FALSE, case-insensitive, untrimmed.</summary>
    public static CellValue ToBoolean(CellValue v) => v.Kind switch
    {
        ValueKind.Boolean => v,
        ValueKind.Blank => CellValue.FromBoolean(false),
        ValueKind.Number => CellValue.FromBoolean(v.Number != 0),
        ValueKind.Text when string.Equals(v.Text, "TRUE", StringComparison.OrdinalIgnoreCase) => CellValue.FromBoolean(true),
        ValueKind.Text when string.Equals(v.Text, "FALSE", StringComparison.OrdinalIgnoreCase) => CellValue.FromBoolean(false),
        ValueKind.Text => CellValue.FromError(ErrorCodes.Value),
        _ => v,
    };

    /// <summary>
    /// Integer parameter (§6.9): coerce to number, truncate toward zero. Values outside the int range saturate.
    /// On failure <paramref name="error"/> is the coercion error.
    /// </summary>
    public static bool TryToInt(CellValue v, out int value, out CellValue error)
    {
        var n = ToNumber(v);
        if (n.Kind == ValueKind.Error) { value = 0; error = n; return false; }
        double t = Math.Truncate(n.Number);
        value = t >= int.MaxValue ? int.MaxValue : t <= int.MinValue ? int.MinValue : (int)t;
        error = default;
        return true;
    }
}
