using System.Globalization;
using System.Windows;
using Gridline.Core;

namespace Gridline.App;

/// <summary>Display text and alignment for a cell value (§11.3).</summary>
public static class CellFormatter
{
    public static (string Text, TextAlignment Align) Format(CellValue v) => v.Kind switch
    {
        ValueKind.Number => (v.Number.ToString("G10", CultureInfo.InvariantCulture), TextAlignment.Right),
        ValueKind.Text => (v.Text, TextAlignment.Left),
        ValueKind.Boolean => (v.Boolean ? "TRUE" : "FALSE", TextAlignment.Center),
        ValueKind.Error => (v.Error, TextAlignment.Center),
        _ => ("", TextAlignment.Left),
    };
}
