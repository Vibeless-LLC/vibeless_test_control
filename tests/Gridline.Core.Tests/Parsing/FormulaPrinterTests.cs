using Gridline.Core.Parsing;

namespace Gridline.Core.Tests.Parsing;

public class FormulaPrinterTests
{
    private sealed class FakeArity : IFunctionArity
    {
        public bool TryGetArity(string upperName, out int min, out int max)
        {
            (min, max) = upperName switch
            {
                "SUM" => (1, 255),
                "IF" => (2, 3),
                _ => (-1, -1),
            };
            return min >= 0;
        }
    }

    private static readonly IFunctionArity Arity = new FakeArity();

    private static string Canon(string f) => FormulaPrinter.Print(Parser.Parse(f[1..], Arity).Root!, _ => null);
    private static string Shift(string body, int dr, int dc, out bool changed) =>
        FormulaPrinter.Print(ReferenceRewriter.Shift(Parser.Parse(body, Arity).Root!, dr, dc, out changed), _ => null);

    [Fact] public void SpecExample() => Assert.Equal("=SUM(A1,$B$2:C3)*(2.50+x)", Canon("= sum( a1 , $b$2:C3 ) * (2.50+ x)"));
    [Fact] public void StringsEscaped() => Assert.Equal("=\"a\"\"b\"&\"\"", Canon("=\"a\"\"b\" & \"\""));
    [Fact] public void OmittedArgs() => Assert.Equal("=IF(A1,,3)", Canon("=if(a1, ,3)"));
    [Fact] public void ParensPreservedNotAdded() => Assert.Equal("=((1))+-2^2%", Canon("=((1)) + -2^2%"));
    [Fact] public void BoolsErrorsUnknownFuncsUpper() => Assert.Equal("=FOO(TRUE,#N/A)", Canon("=foo(true,#N/A)"));
    [Fact] public void NameCasingFromDefinition() => Assert.Equal("=Rate*2", FormulaPrinter.Print(Parser.Parse("RATE*2", Arity).Root!, n => n == "RATE" ? "Rate" : null));
    [Fact] public void StringWithSpacesKept() => Assert.Equal("=\" a b \"", Canon("= \" a b \" "));

    [Fact] public void Shift_RelativeAbsoluteMixed() { var n = ReferenceRewriter.Shift(Parser.Parse("A1+$A1+A$1+$A$1", Arity).Root!, 2, 1, out var ch); Assert.True(ch); Assert.Equal("=B3+$A3+B$1+$A$1", FormulaPrinter.Print(n, _ => null)); }
    [Fact] public void Shift_OffGrid_Ref() => Assert.Equal("=#REF!+1", Shift("A1+1", -1, 0, out _));
    [Fact] public void Shift_RangeCornerOffGrid_WholeRef() => Assert.Equal("=SUM(#REF!)", Shift("SUM(A1:B2)", 0, 701, out _));
    [Fact] public void Shift_NoRefs_Unchanged() { Assert.Equal("=1+2", Shift("1+2", 5, 5, out var ch)); Assert.False(ch); }
    [Fact] public void Shift_AllAbsolute_Unchanged() { Assert.Equal("=$A$1", Shift("$A$1", 5, 5, out var ch)); Assert.False(ch); }
    [Fact] public void Shift_ZeroOffset_Unchanged() { Assert.Equal("=A1", Shift("a1", 0, 0, out var ch)); Assert.False(ch); }
    [Fact] public void Shift_OffGridAbsolute_BecomesRef() { Assert.Equal("=#REF!", Shift("A1", -1, 0, out var ch)); Assert.True(ch); }
    [Fact] public void Shift_MaxRowBoundary()
    {
        Assert.Equal("=A100000", Shift("A99999", 1, 0, out _));
        Assert.Equal("=#REF!", Shift("A100000", 1, 0, out _));
    }
    [Fact] public void Shift_ThroughUnaryPercentParenCall() =>
        Assert.Equal("=-(SUM(B2,,C3:D4))%", Shift("-(SUM(A1,,B2:C3))%", 1, 1, out _));

    [Fact]
    public void DeepLeftChain_16kTerms_PrintAndShift()
    {
        const int n = 16_000;
        var sb = new System.Text.StringBuilder("A1");
        for (int i = 1; i < n; i++) sb.Append("+A1");
        var root = Parser.Parse(sb.ToString(), Arity).Root!;
        var shifted = ReferenceRewriter.Shift(root, 1, 0, out var ch);
        Assert.True(ch);
        var text = FormulaPrinter.Print(shifted, _ => null);
        Assert.Equal("=A2" + string.Concat(Enumerable.Repeat("+A2", n - 1)), text);
    }
}
