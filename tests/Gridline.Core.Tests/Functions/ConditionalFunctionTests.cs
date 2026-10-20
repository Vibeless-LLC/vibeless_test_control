namespace Gridline.Core.Tests.Functions;

/// <summary>SUMIF, COUNTIF, AVERAGEIF and their criteria (§7.5).</summary>
public class ConditionalFunctionTests
{
    private static readonly (string, string)[] Fixture =
    [
        ("A1", "1"), ("A2", "'5"), ("A3", "x"), ("A5", "=1/0"), ("A6", "XY"),
        ("B1", "10"), ("B2", "20"), ("B3", "30"), ("B4", "40"), ("B5", "50"), ("B6", "60"),
    ];

    [Theory]
    [InlineData("COUNTIF(A1:A6,\">0\")", "N:1")] [InlineData("COUNTIF(A1:A6,5)", "N:0")] [InlineData("COUNTIF(A1:A6,\"x*\")", "N:2")] [InlineData("COUNTIF(A1:A6,\"X\")", "N:1")]
    [InlineData("COUNTIF(A1:A6,\"\")", "N:1")] [InlineData("COUNTIF(A1:A6,\"=\")", "N:1")] [InlineData("COUNTIF(A1:A6,\"<>\")", "N:4")] [InlineData("COUNTIF(A1:A6,\"<y\")", "N:3")]
    [InlineData("SUMIF(A1:A6,\"x*\",B1)", "N:90")] [InlineData("SUMIF(A1:A6,1,B1:B6)", "N:10")] [InlineData("SUMIF(A1:A6,\"<>x\",B1:B6)", "N:130")] [InlineData("SUMIF(B1:B6,\">25\")", "N:180")]
    [InlineData("AVERAGEIF(A1:A6,\"zzz\")", "E:#DIV/0!")] [InlineData("AVERAGEIF(B1:B6,\">=40\")", "N:50")] [InlineData("COUNTIF(A1:A6,1/0)", "E:#DIV/0!")]
    // additional edges
    [InlineData("COUNTIF(A1:A6,\"5\")", "N:0")] [InlineData("COUNTIF(A1:A6,\"=5\")", "N:0")] [InlineData("COUNTIF(A1:A6,\"<>5\")", "N:1")]
    [InlineData("COUNTIF(A1:A6,\"=x\")", "N:1")] [InlineData("COUNTIF(A1:A6,\"<=x\")", "N:2")] [InlineData("COUNTIF(A1:A6,\">x\")", "N:1")] [InlineData("COUNTIF(A1:A6,\"?\")", "N:2")]
    [InlineData("COUNTIF(A1:A6,TRUE)", "N:0")] [InlineData("COUNTIF(A1:A6,A4)", "N:0")] [InlineData("COUNTIF(A1:A6,\"<>x\")", "N:4")] [InlineData("COUNTIF(A1:A6,\"<>\"&\"\")", "N:4")]
    [InlineData("COUNTIF(B1:B6,\"<=30\")", "N:3")] [InlineData("COUNTIF(B1:B6,30)", "N:1")] [InlineData("COUNTIF(B1:B6,\"=30\")", "N:1")] [InlineData("COUNTIF(B1:B6,\"<>30\")", "N:5")]
    [InlineData("COUNTIF(5,1)", "E:#VALUE!")] [InlineData("COUNTIF(A1:A6,)", "E:#VALUE!")]
    [InlineData("SUMIF(A1:A6,\"\",B1:B6)", "N:40")] [InlineData("SUMIF(A1:A6,\"x\",B1:B6)", "N:30")] [InlineData("SUMIF(A1:A6,\"=\",B3:B6)", "N:60")]
    [InlineData("SUMIF(A1:A6,\"x\",B1:B2)", "N:30")] [InlineData("SUMIF(A1:A6,\"XY\",B6:B7)", "N:0")]
    [InlineData("SUMIF(A1:A6,\"XY\",B100000)", "N:0")] [InlineData("SUMIF(A1:A6,1,5)", "E:#VALUE!")] [InlineData("SUMIF(5,1)", "E:#VALUE!")]
    [InlineData("AVERAGEIF(A1:A6,\"<>x\",B1:B6)", "N:32.5")] [InlineData("AVERAGEIF(B1:B6,\"<>\")", "N:35")]
    public void Conditional(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f, Fixture));

    private static readonly (string, string)[] ZeroFixture =
    [
        ("A1", "0"), ("A3", "5"), ("B1", "10"), ("B2", "20"), ("B3", "30"),
    ];

    [Fact]
    public void BlankCriteriaValue_IsNumericZero()
    {
        Assert.Equal("N:1", CoreTestHost.Eval("=COUNTIF(A1:A3,C1)", ZeroFixture));
        Assert.Equal("N:10", CoreTestHost.Eval("=SUMIF(A1:A3,C1,B1:B3)", ZeroFixture));
        Assert.Equal("N:1", CoreTestHost.Eval("=COUNTIF(A1:A3,\"\")", ZeroFixture));
    }

    [Fact]
    public void Criteria_NotEqualText_IncludesBlanksExcludesErrors() =>
        Assert.Equal("N:4", CoreTestHost.Eval("=COUNTIF(A1:A6,\"<>x\")", Fixture));

    [Fact]
    public void SumIf_ErrorInSumRangeAtMatchedPosition_Propagates()
    {
        var f = new (string, string)[] { ("A1", "a"), ("A2", "b"), ("B1", "1"), ("B2", "=1/0") };
        Assert.Equal("E:#DIV/0!", CoreTestHost.Eval("=SUMIF(A1:A2,\"b\",B1:B2)", f));
        Assert.Equal("N:1", CoreTestHost.Eval("=SUMIF(A1:A2,\"a\",B1:B2)", f));
        Assert.Equal("E:#DIV/0!", CoreTestHost.Eval("=SUMIF(A1:A2,\"<>x\",B1:B2)", f)); // blank-matching path
        Assert.Equal("E:#DIV/0!", CoreTestHost.Eval("=AVERAGEIF(A1:A2,\"b\",B1:B2)", f));
    }

    [Fact]
    public void Booleans_MatchBooleanCellsOnly()
    {
        var f = new (string, string)[] { ("A1", "TRUE"), ("A2", "FALSE"), ("A3", "1"), ("A4", "true") , ("B1", "1"), ("B2", "2"), ("B3", "4"), ("B4", "8") };
        Assert.Equal("N:2", CoreTestHost.Eval("=COUNTIF(A1:A4,TRUE)", f));
        Assert.Equal("N:2", CoreTestHost.Eval("=COUNTIF(A1:A4,\"TRUE\")", f));
        Assert.Equal("N:3", CoreTestHost.Eval("=COUNTIF(A1:A4,\">=FALSE\")", f));
        Assert.Equal("N:9", CoreTestHost.Eval("=SUMIF(A1:A4,\"=true\",B1:B4)", f));
        Assert.Equal("N:1", CoreTestHost.Eval("=COUNTIF(A1:A4,1)", f));
    }

    [Fact]
    public void Wildcards_Escape()
    {
        var f = new (string, string)[] { ("A1", "a*b"), ("A2", "axb"), ("A3", "a?b") };
        Assert.Equal("N:3", CoreTestHost.Eval("=COUNTIF(A1:A3,\"a?b\")", f));
        Assert.Equal("N:1", CoreTestHost.Eval("=COUNTIF(A1:A3,\"a~*b\")", f));
        Assert.Equal("N:1", CoreTestHost.Eval("=COUNTIF(A1:A3,\"a~?b\")", f));
    }

    [Fact]
    public void HugeBlankRange_IsNotScanned()
    {
        var wb = new Workbook();
        wb.SetCell("A5", "x");
        wb.SetCell("B5", "7");
        wb.SetCell("D1", "=COUNTIF(A1:B100000,\"\")");
        wb.SetCell("D2", "=SUMIF(A1:A100000,\"<>x\",B1:B100000)");
        wb.SetCell("D3", "=SUMIF(A1:A100000,\"\",B1:B100000)");
        Assert.Equal("N:199998", wb.GetValue("D1").ToCanonicalString());
        Assert.Equal("N:0", wb.GetValue("D2").ToCanonicalString()); // row 5 is the only non-blank B and A5 is "x"
        Assert.Equal("N:0", wb.GetValue("D3").ToCanonicalString());

    }

    [Fact]
    public void NotEqualBoolean_MatchesEverythingButThatBoolean()
    {
        var f = new (string, string)[] { ("A1", "1"), ("A2", "x"), ("B1", "FALSE") };
        Assert.Equal("N:8", CoreTestHost.Eval("=COUNTIF(A1:B4,\"<>TRUE\")", f));
        Assert.Equal("N:7", CoreTestHost.Eval("=COUNTIF(A1:B4,\"<>FALSE\")", f));
        var g = new (string, string)[] { ("A1", "TRUE"), ("A2", "=1/0") };
        Assert.Equal("N:2", CoreTestHost.Eval("=COUNTIF(A1:B2,\"<>TRUE\")", g)); // B1, B2 blank; A1 TRUE and A2 error excluded
    }
}
