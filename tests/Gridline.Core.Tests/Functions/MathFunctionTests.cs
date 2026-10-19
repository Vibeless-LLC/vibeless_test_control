namespace Gridline.Core.Tests.Functions;

public class MathFunctionTests
{
    [Theory]
    [InlineData("ROUND(2.675,2)", "N:2.68")] [InlineData("ROUND(0.285,2)", "N:0.29")] [InlineData("ROUND(-2.5,0)", "N:-3")] [InlineData("ROUND(1250,-2)", "N:1300")]
    [InlineData("ROUNDUP(1.201,2)", "N:1.21")] [InlineData("ROUNDUP(-1.201,2)", "N:-1.21")] [InlineData("ROUNDDOWN(-1.209,2)", "N:-1.2")] [InlineData("ROUND(5,-1)", "N:10")] [InlineData("ROUND(4,-1)", "N:0")] [InlineData("ROUND(1.5,1.9)", "N:1.5")] [InlineData("ROUND(123,-5)", "N:0")]
    [InlineData("INT(-2.5)", "N:-3")] [InlineData("MOD(-3,2)", "N:1")] [InlineData("MOD(3,-2)", "N:-1")] [InlineData("MOD(1,0)", "E:#DIV/0!")]
    [InlineData("SQRT(-1)", "E:#NUM!")] [InlineData("SIGN(-0.1)", "N:-1")] [InlineData("POWER(0,0)", "E:#NUM!")] [InlineData("ABS(-3)", "N:3")] [InlineData("PI()", "N:3.141592653589793")]
    [InlineData("SUM()", "E:#PARSE!")] [InlineData("SUM(1,\"2\",TRUE)", "N:4")] [InlineData("SUM(\"x\")", "E:#VALUE!")] [InlineData("AVERAGE(\"x\"&\"\")", "E:#VALUE!")]
    // Extra rounding edge cases.
    [InlineData("ROUNDUP(0.001,-2)", "N:100")] [InlineData("ROUNDUP(-0.001,-2)", "N:-100")] [InlineData("ROUNDDOWN(999,-5)", "N:0")]
    [InlineData("ROUND(9.995,2)", "N:10")] [InlineData("ROUND(99.5,0)", "N:100")] [InlineData("ROUND(1E-20,3)", "N:0")]
    [InlineData("ROUNDUP(1.0000000000000002,2)", "N:1.01")] [InlineData("ROUNDDOWN(1234.5678,-2)", "N:1200")] [InlineData("ROUND(1.005,2)", "N:1.01")]
    [InlineData("ROUND(1E+300,2)", "N:1E+300")] [InlineData("ROUND(0,2)", "N:0")]
    public void Direct(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f));

    [Theory]
    [InlineData("ROUND(1.5,)", "E:#VALUE!")] [InlineData("ROUND(,1)", "E:#VALUE!")] [InlineData("ROUNDUP(1.5,)", "E:#VALUE!")] [InlineData("ROUNDDOWN(,1)", "E:#VALUE!")] [InlineData("MOD(5,)", "E:#VALUE!")] [InlineData("MOD(,5)", "E:#VALUE!")]
    [InlineData("POWER(2,)", "E:#VALUE!")] [InlineData("POWER(,2)", "E:#VALUE!")]
    [InlineData("AVERAGE(1,)", "N:1")] [InlineData("SUM(,1)", "N:1")] [InlineData("PRODUCT(2,,3)", "N:6")] [InlineData("MIN(,5)", "N:5")] [InlineData("SUMPRODUCT(,2)", "N:2")] [InlineData("SUMPRODUCT(,)", "N:0")] [InlineData("COUNT(,1)", "N:1")] [InlineData("COUNTA(,1)", "N:1")]
    public void OmittedArgs(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f));


    [Fact] public void Agg_ReferenceIgnoresTextBoolBlank() => Assert.Equal("N:5", CoreTestHost.Eval("=SUM(A1:A4)", ("A1","5"), ("A2","'7"), ("A3","TRUE")));
    [Fact] public void Agg_ReferenceErrorPropagates() => Assert.Equal("E:#DIV/0!", CoreTestHost.Eval("=SUM(A1:A2)", ("A1","5"), ("A2","=1/0")));
    [Fact] public void Average_NoNumbers() => Assert.Equal("E:#DIV/0!", CoreTestHost.Eval("=AVERAGE(A1:A3)"));
    [Fact] public void MinMaxProduct_NoNumbers_Zero() { Assert.Equal("N:0", CoreTestHost.Eval("=MIN(A1:A3)")); Assert.Equal("N:0", CoreTestHost.Eval("=MAX(A1)")); Assert.Equal("N:0", CoreTestHost.Eval("=PRODUCT(A1:A2)")); }
    [Fact] public void Count_RefIgnoresErrors_DirectCountsBoolAndNumericText() { Assert.Equal("N:1", CoreTestHost.Eval("=COUNT(A1:A3)", ("A1","1"), ("A2","=1/0"), ("A3","'2"))); Assert.Equal("N:3", CoreTestHost.Eval("=COUNT(1,TRUE,\"3\",\"x\",1/0)")); }
    [Fact] public void CountA_IncludesErrorsAndEmptyText() => Assert.Equal("N:3", CoreTestHost.Eval("=COUNTA(A1:A4)", ("A1","1"), ("A2","=1/0"), ("A3","=\"\"")));
    [Fact] public void CountBlank_CountsEmptyTextFormulas() => Assert.Equal("N:3", CoreTestHost.Eval("=COUNTBLANK(A1:A4)", ("A1","1"), ("A2","=\"\"")));
    [Fact] public void CountBlank_DirectArgIsValueError() => Assert.Equal("E:#VALUE!", CoreTestHost.Eval("=COUNTBLANK(1)"));
    [Fact] public void SumProduct() { Assert.Equal("N:11", CoreTestHost.Eval("=SUMPRODUCT(A1:A2,B1:B2)", ("A1","1"),("A2","2"),("B1","3"),("B2","4"))); Assert.Equal("E:#VALUE!", CoreTestHost.Eval("=SUMPRODUCT(A1:A2,B1:B3)")); Assert.Equal("N:0", CoreTestHost.Eval("=SUMPRODUCT(A1:A2,B1:B2)", ("A1","'x"),("B1","3"))); }
    [Fact] public void SumProduct_ErrorPropagates() => Assert.Equal("E:#DIV/0!", CoreTestHost.Eval("=SUMPRODUCT(A1:A2)", ("A1","1"), ("A2","=1/0")));
    [Fact] public void SumProduct_ScalarIs1x1() => Assert.Equal("N:6", CoreTestHost.Eval("=SUMPRODUCT(2,3)"));
    [Fact] public void SumSq() => Assert.Equal("N:25", CoreTestHost.Eval("=SUMSQ(3,4)"));
    [Fact] public void FunctionNamesCaseInsensitive() => Assert.Equal("N:3", CoreTestHost.Eval("=sUm(1,2)"));
    [Fact] public void MinMaxAverage_Values() { Assert.Equal("N:1", CoreTestHost.Eval("=MIN(A1:A3)", ("A1","3"), ("A2","1"), ("A3","2"))); Assert.Equal("N:3", CoreTestHost.Eval("=MAX(A1:A3)", ("A1","3"), ("A2","1"), ("A3","2"))); Assert.Equal("N:2", CoreTestHost.Eval("=AVERAGE(A1:A3)", ("A1","3"), ("A2","1"), ("A3","2"))); }
}
