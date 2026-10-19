namespace Gridline.Core.Tests.Functions;

public class LogicFunctionTests
{
    [Theory]
    [InlineData("IF(TRUE,1,1/0)", "N:1")] [InlineData("IF(FALSE,1/0,2)", "N:2")] [InlineData("IF(FALSE,1)", "B:FALSE")] [InlineData("IF(TRUE,,1)", "N:0")] [InlineData("IF(\"x\",1,2)", "E:#VALUE!")] [InlineData("IF(\"true\",1,2)", "N:1")]
    [InlineData("AND(TRUE,1)", "B:TRUE")] [InlineData("OR(0,FALSE)", "B:FALSE")] [InlineData("XOR(TRUE,TRUE,TRUE)", "B:TRUE")] [InlineData("AND(\"x\")", "E:#VALUE!")]
    [InlineData("NOT(0)", "B:TRUE")] [InlineData("IFERROR(1/0,7)", "N:7")] [InlineData("IFERROR(1,1/0)", "N:1")] [InlineData("IFNA(#N/A,1)", "N:1")] [InlineData("IFNA(#DIV/0!,1)", "E:#DIV/0!")]
    [InlineData("ISNUMBER(\"1\")", "B:FALSE")] [InlineData("ISTEXT(\"\")", "B:TRUE")] [InlineData("ISLOGICAL(1)", "B:FALSE")] [InlineData("ISERROR(#N/A)", "B:TRUE")] [InlineData("ISNA(#REF!)", "B:FALSE")] [InlineData("ISBLANK(\"\")", "B:FALSE")]
    // Extras.
    [InlineData("IF(1/0,1,2)", "E:#DIV/0!")] [InlineData("IF(FALSE,1,)", "B:FALSE")] [InlineData("XOR(TRUE,FALSE)", "B:TRUE")] [InlineData("NOT(\"x\")", "E:#VALUE!")] [InlineData("AND(1/0)", "E:#DIV/0!")]
    [InlineData("ISNA(#N/A)", "B:TRUE")] [InlineData("ISERROR(1)", "B:FALSE")] [InlineData("ISNUMBER(1)", "B:TRUE")] [InlineData("ISLOGICAL(FALSE)", "B:TRUE")] [InlineData("ISTEXT(1)", "B:FALSE")] [InlineData("ISBLANK(1)", "B:FALSE")]
    public void Direct(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f));

    [Fact] public void AndOr_RefIgnoresTextAndBlank() { Assert.Equal("B:TRUE", CoreTestHost.Eval("=AND(A1:A3)", ("A1","TRUE"), ("A2","'x"))); Assert.Equal("E:#VALUE!", CoreTestHost.Eval("=OR(A1:A2)", ("A1","'x"))); Assert.Equal("B:TRUE", CoreTestHost.Eval("=OR(A1)", ("A1","2"))); }
    [Fact] public void IsBlank_TrueOnlyForTrulyBlankRef() { Assert.Equal("B:TRUE", CoreTestHost.Eval("=ISBLANK(A1)")); Assert.Equal("B:FALSE", CoreTestHost.Eval("=ISBLANK(A1)", ("A1","=\"\""))); }
    [Fact] public void IsFunctions_NeverError() => Assert.Equal("B:FALSE", CoreTestHost.Eval("=ISNUMBER(A1:A2)"));
}
