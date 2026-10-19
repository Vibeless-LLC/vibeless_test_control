using Gridline.Core.Evaluation;

namespace Gridline.Core.Tests.Evaluation;

public class CoercionTests
{
    [Theory]
    [InlineData("1", 1.0)] [InlineData("-1.5e3", -1500.0)] [InlineData(".5", 0.5)] [InlineData("5.", 5.0)]
    [InlineData("+3%", 0.03)] [InlineData("1E+2", 100.0)] [InlineData("-0", 0.0)] [InlineData("1e-999", 0.0)]
    public void NumberGrammar_Accepts(string s, double expected)
    {
        Assert.True(NumberGrammar.TryParse(s, out var v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("1,000")] [InlineData(" 5")] [InlineData("$5")] [InlineData("1e")] [InlineData(".")] [InlineData("1e999")]
    [InlineData("--1")] [InlineData("")] [InlineData("5 ")] [InlineData("5%%")] [InlineData("e5")] [InlineData("1.2.3")]
    [InlineData("١")] [InlineData("Infinity")] [InlineData("NaN")] [InlineData("+")] [InlineData("%")] [InlineData("1e+")]
    public void NumberGrammar_Rejects(string s) => Assert.False(NumberGrammar.TryParse(s, out _));

    [Theory]
    [InlineData(" 5 ", "N:5")] [InlineData("5%", "N:0.05")] [InlineData("", "E:#VALUE!")] [InlineData("   ", "E:#VALUE!")]
    [InlineData("\t5", "E:#VALUE!")] [InlineData("abc", "E:#VALUE!")] [InlineData("2024-01-01", "E:#VALUE!")]
    public void TextToNumber(string text, string expected) =>
        Assert.Equal(expected, Coercion.ToNumber(CellValue.FromText(text)).ToCanonicalString());

    [Fact]
    public void ToNumber_OtherKinds()
    {
        Assert.Equal("N:0", Coercion.ToNumber(CellValue.Blank).ToCanonicalString());
        Assert.Equal("N:1", Coercion.ToNumber(CellValue.FromBoolean(true)).ToCanonicalString());
        Assert.Equal("N:0", Coercion.ToNumber(CellValue.FromBoolean(false)).ToCanonicalString());
        Assert.Equal("N:2.5", Coercion.ToNumber(CellValue.FromNumber(2.5)).ToCanonicalString());
        Assert.Equal("E:#N/A", Coercion.ToNumber(CellValue.FromError(ErrorCodes.NA)).ToCanonicalString());
    }

    [Fact]
    public void ToText_AllKinds()
    {
        Assert.Equal("T:", Coercion.ToText(CellValue.Blank).ToCanonicalString());
        Assert.Equal("T:TRUE", Coercion.ToText(CellValue.FromBoolean(true)).ToCanonicalString());
        Assert.Equal("T:FALSE", Coercion.ToText(CellValue.FromBoolean(false)).ToCanonicalString());
        Assert.Equal("T:0.3", Coercion.ToText(CellValue.FromNumber(0.1 + 0.2)).ToCanonicalString());
        Assert.Equal("T:1E+15", Coercion.ToText(CellValue.FromNumber(1e15)).ToCanonicalString());
        Assert.Equal("T:x", Coercion.ToText(CellValue.FromText("x")).ToCanonicalString());
        Assert.Equal("E:#REF!", Coercion.ToText(CellValue.FromError(ErrorCodes.Ref)).ToCanonicalString());
    }

    [Fact]
    public void ToBoolean_AllKinds()
    {
        Assert.Equal("B:FALSE", Coercion.ToBoolean(CellValue.Blank).ToCanonicalString());
        Assert.Equal("B:TRUE", Coercion.ToBoolean(CellValue.FromNumber(-2)).ToCanonicalString());
        Assert.Equal("B:FALSE", Coercion.ToBoolean(CellValue.FromNumber(0)).ToCanonicalString());
        Assert.Equal("B:TRUE", Coercion.ToBoolean(CellValue.FromText("tRuE")).ToCanonicalString());
        Assert.Equal("B:FALSE", Coercion.ToBoolean(CellValue.FromText("false")).ToCanonicalString());
        Assert.Equal("E:#VALUE!", Coercion.ToBoolean(CellValue.FromText("yes")).ToCanonicalString());
        Assert.Equal("E:#VALUE!", Coercion.ToBoolean(CellValue.FromText(" TRUE")).ToCanonicalString());
        Assert.Equal("B:TRUE", Coercion.ToBoolean(CellValue.FromBoolean(true)).ToCanonicalString());
        Assert.Equal("E:#NUM!", Coercion.ToBoolean(CellValue.FromError(ErrorCodes.Num)).ToCanonicalString());
    }

    private static CellValue V(string kind, string s) => kind switch
    {
        "N" => CellValue.FromNumber(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)),
        "T" => CellValue.FromText(s),
        "B" => CellValue.FromBoolean(s == "TRUE"),
        _ => CellValue.Blank,
    };

    [Theory]
    [InlineData("N", "1", "N", "2", -1)] [InlineData("N", "2", "N", "2", 0)] [InlineData("N", "3", "N", "2", 1)]
    [InlineData("N", "1e308", "T", "", -1)] [InlineData("T", "a", "B", "FALSE", -1)] [InlineData("N", "5", "B", "FALSE", -1)]
    [InlineData("T", "abc", "T", "ABD", -1)] [InlineData("T", "A", "T", "a", 0)] [InlineData("T", "b", "T", "A", 1)]
    [InlineData("B", "FALSE", "B", "TRUE", -1)] [InlineData("B", "TRUE", "B", "TRUE", 0)]
    [InlineData("_", "", "_", "", 0)] [InlineData("_", "", "N", "0", 0)] [InlineData("_", "", "T", "", 0)] [InlineData("_", "", "B", "FALSE", 0)]
    [InlineData("_", "", "N", "-1", 1)] [InlineData("_", "", "T", "a", -1)] [InlineData("B", "TRUE", "_", "", 1)]
    [InlineData("T", "_", "T", "a", 1)] // ordinal on uppercase: '_' (0x5F) > 'A' (0x41), unlike culture collation
    public void Compare(string ka, string a, string kb, string b, int expected) =>
        Assert.Equal(expected, Comparison.Compare(V(ka, a), V(kb, b)));

    [Fact]
    public void Compare_ErrorThrows() =>
        Assert.Throws<ArgumentException>(() => Comparison.Compare(CellValue.FromError(ErrorCodes.NA), CellValue.FromNumber(1)));
}
