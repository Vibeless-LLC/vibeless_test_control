using Gridline.Core.Parsing;

namespace Gridline.Core.Tests.Parsing;

public class ParserTests
{
    private sealed class FakeArity : IFunctionArity
    {
        public bool TryGetArity(string upperName, out int min, out int max)
        {
            (min, max) = upperName switch
            {
                "SUM" => (1, 255),
                "PI" => (0, 0),
                "IF" => (2, 3),
                "NOT" => (1, 1),
                _ => (-1, -1),
            };
            return min >= 0;
        }
    }

    private static ParsedFormula P(string s) => Parser.Parse(s, new FakeArity());

    [Theory]
    [InlineData("1+")] [InlineData("(1")] [InlineData("1)")] [InlineData("")] [InlineData("SUM (A1)")] [InlineData("A1:1")] [InlineData("(A1):B2")] [InlineData("A1:B2:C3")] [InlineData("Rate:A1")]
    [InlineData("PI(1)")] [InlineData("IF(1)")] [InlineData("NOT()")] [InlineData("1 2")]
    [InlineData("1e")] [InlineData("1e999")] [InlineData("\"abc")] [InlineData("SUM(1,")] [InlineData("F(1))")]
    public void ParseErrors(string s)
    {
        var r = P(s);
        Assert.True(r.IsParseError);
        Assert.Null(r.Root);
    }

    [Fact] public void UnknownFunction_Parses() => Assert.False(P("FOO(1,2)").IsParseError);
    [Fact] public void OmittedArgs() { var p = (CallNode)P("IF(A1,,3)").Root!; Assert.Null(p.Args[1]); Assert.Equal(3, p.Args.Count); }
    [Fact] public void EmptyArgsIsZero() { var p = (CallNode)P("PI()").Root!; Assert.Empty(p.Args); }
    [Fact] public void OmittedCountsTowardArity() { Assert.False(P("IF(,)").IsParseError); Assert.True(P("NOT(,)").IsParseError); Assert.False(P("F(,)").IsParseError); Assert.Equal(2, ((CallNode)P("F(,)").Root!).Args.Count); }
    [Fact] public void CallNameAsWritten() => Assert.Equal("Sum", ((CallNode)P("Sum(1)").Root!).Name);
    [Fact] public void UnaryBindsTighterThanPower() { var b = (BinaryNode)P("-2^2").Root!; Assert.Equal("^", b.Op); Assert.IsType<UnaryNode>(b.Left); }
    [Fact] public void PowerLeftAssoc() { var b = (BinaryNode)P("2^3^2").Root!; Assert.IsType<BinaryNode>(b.Left); }
    [Fact] public void PercentTwice() => Assert.IsType<PercentNode>(((PercentNode)P("50%%").Root!).Operand);
    [Fact] public void ParensRetained() => Assert.IsType<ParenNode>(P("(1)").Root);
    [Fact] public void ConcatBelowAdditive() { var b = (BinaryNode)P("1+2&3").Root!; Assert.Equal("&", b.Op); }
    [Fact] public void CompareBelowConcat() { var b = (BinaryNode)P("\"a\"&1=1").Root!; Assert.Equal("=", b.Op); }
    [Fact] public void RefAbsFlags() { var r = (RefNode)P("$B7").Root!; Assert.True(r.ColAbs); Assert.False(r.RowAbs); Assert.Equal(7, r.Row); Assert.Equal(2, r.Col); }
    [Fact] public void RowAbsFlag() { var r = (RefNode)P("B$7").Root!; Assert.False(r.ColAbs); Assert.True(r.RowAbs); }
    [Fact] public void RangeKeptAsWritten() { var r = (RangeNode)P("B2:A1").Root!; Assert.Equal(2, r.First.Row); Assert.Equal(1, r.Second.Row); }
    [Fact] public void NumberLexemeKept() { var n = (NumberNode)P("1.50").Root!; Assert.Equal("1.50", n.Lexeme); Assert.Equal(1.5, n.Value); }
    [Fact] public void Literals() { Assert.Equal("a\"b", ((StringNode)P("\"a\"\"b\"").Root!).Value); Assert.True(((BoolNode)P("true").Root!).Value); Assert.Equal("#N/A", ((ErrorNode)P("#N/A").Root!).Code); Assert.Equal("Rate", ((NameNode)P("Rate").Root!).Name); }
    [Fact] public void LeftAssocSubtract() { var b = (BinaryNode)P("1-2-3").Root!; Assert.IsType<BinaryNode>(b.Left); Assert.IsType<NumberNode>(b.Right); }
    [Fact] public void PercentAfterUnary() { var p = (PercentNode)P("-2%").Root!; Assert.IsType<UnaryNode>(p.Operand); }
    [Fact] public void ArgCountMax255() { Assert.False(P("SUM(" + string.Join(",", Enumerable.Repeat("1", 255)) + ")").IsParseError); Assert.True(P("SUM(" + string.Join(",", Enumerable.Repeat("1", 256)) + ")").IsParseError); }
    [Fact] public void ArityCaseInsensitive() => Assert.True(P("pi(1)").IsParseError);

    /// <summary>§5.4: deep nesting must not crash the process; beyond Parser.MaxDepth it is a parse error.</summary>
    [Fact]
    public void DeepNesting20000_NoCrash()
    {
        string body = new string('(', 20_000) + "1" + new string(')', 20_000);
        Assert.True(P(body).IsParseError);

        // End to end through the Workbook (parse, dependency extraction, evaluation), as deep as the 32,767 input limit allows.
        string deepest = "=" + new string('(', 16_382) + "1" + new string(')', 16_382);
        Assert.Equal(Workbook.MaxInputLength - 1, deepest.Length);
        Assert.Equal("E:#PARSE!", CoreTestHost.Eval(deepest));
    }

    private static string Parens(int n) => new string('(', n) + "1" + new string(')', n);

    [Fact]
    public void Depth1024Parens_Parses()
    {
        Assert.Equal(1024, Parser.MaxDepth);
        Assert.False(P(Parens(Parser.MaxDepth)).IsParseError);
        Assert.True(P(Parens(Parser.MaxDepth + 1)).IsParseError);
        Assert.Equal("N:1", CoreTestHost.Eval("=" + Parens(Parser.MaxDepth)));
    }

    [Fact] public void DeepUnaryChain_ParseErrorBeyondMax() => Assert.True(P(new string('-', 5000) + "1").IsParseError);
    [Fact] public void UnaryChain_AtMax_Parses()
    {
        Assert.False(P(new string('-', Parser.MaxDepth) + "1").IsParseError);
        Assert.True(P(new string('-', Parser.MaxDepth + 1) + "1").IsParseError);
    }
    [Fact] public void DeepPercentChain_ParseErrorBeyondMax() => Assert.True(P("1" + new string('%', 5000)).IsParseError);
    [Fact] public void PercentChain_AtMax_Parses()
    {
        Assert.False(P("1" + new string('%', Parser.MaxDepth)).IsParseError);
        Assert.True(P("1" + new string('%', Parser.MaxDepth + 1)).IsParseError);
    }
    [Fact] public void PercentOverDeepParens_CountsBoth() =>
        Assert.True(P(Parens(Parser.MaxDepth - 1) + "%%").IsParseError);
    [Fact] public void DeepNestedCalls_ParseErrorBeyondMax() =>
        Assert.True(P(string.Concat(Enumerable.Repeat("SUM(", 2000)) + "1" + new string(')', 2000)).IsParseError);

    [Fact]
    public void LeftDeepChain_StillParses()
    {
        string f = "1" + string.Concat(Enumerable.Repeat("+1", 15999));
        Assert.False(P(f).IsParseError);
        Assert.Equal("N:16000", CoreTestHost.Eval("=" + f));
    }

    [Fact] public void WorkbookDeepUnary_IsParseError() => Assert.Equal("E:#PARSE!", CoreTestHost.Eval("=" + new string('-', 5000) + "1"));

    [Fact]
    public void PrintAndShift_AtMaxDepth_SmallStack()
    {
        Exception? failure = null;
        string? printed = null;
        var roots = new[] { Parens(Parser.MaxDepth), new string('-', Parser.MaxDepth) + "A1", "A1" + new string('%', Parser.MaxDepth) }
            .Select(b => P(b).Root!).ToArray();
        var t = new Thread(() =>
        {
            try
            {
                foreach (var root in roots)
                {
                    var shifted = ReferenceRewriter.Shift(root, 1, 1, out _);
                    printed = FormulaPrinter.Print(shifted, _ => null);
                }
            }
            catch (Exception e) { failure = e; }
        }, 256 * 1024);
        t.Start();
        t.Join();
        Assert.Null(failure);
        Assert.EndsWith("B2" + new string('%', Parser.MaxDepth), printed);
    }
}
