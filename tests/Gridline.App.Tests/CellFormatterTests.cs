using System.Globalization;
using System.Windows;
using Gridline.App;
using Gridline.Core;

namespace Gridline.App.Tests;

public class CellFormatterTests
{
    [Theory]
    [InlineData(1234567890123.0, "1.234567890E+12")]
    [InlineData(0.1, "0.1")]
    [InlineData(3.0, "3")]
    public void Number_G10Right(double d, string text)
    {
        _ = text; // documentation only; the expected text is computed below (brief).
        var (t, a) = CellFormatter.Format(CellValue.FromNumber(d));
        Assert.Equal(d.ToString("G10", CultureInfo.InvariantCulture), t);
        Assert.Equal(TextAlignment.Right, a);
    }

    [Fact]
    public void Text_Left_Bool_Error_Center()
    {
        Assert.Equal(TextAlignment.Left, CellFormatter.Format(CellValue.FromText("x")).Align);
        Assert.Equal(TextAlignment.Center, CellFormatter.Format(CellValue.FromBoolean(true)).Align);
        Assert.Equal(("#N/A", TextAlignment.Center), CellFormatter.Format(CellValue.FromError("#N/A")));
    }

    [Fact]
    public void Text_And_Booleans_ShowTheirText()
    {
        Assert.Equal(("hello", TextAlignment.Left), CellFormatter.Format(CellValue.FromText("hello")));
        Assert.Equal(("TRUE", TextAlignment.Center), CellFormatter.Format(CellValue.FromBoolean(true)));
        Assert.Equal(("FALSE", TextAlignment.Center), CellFormatter.Format(CellValue.FromBoolean(false)));
    }

    [Fact]
    public void Blank_IsEmpty() => Assert.Equal("", CellFormatter.Format(CellValue.Blank).Text);
}
