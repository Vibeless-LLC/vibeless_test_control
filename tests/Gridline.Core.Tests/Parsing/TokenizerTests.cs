using Gridline.Core.Parsing;

namespace Gridline.Core.Tests.Parsing;

public class TokenizerTests
{
    [Theory]
    [InlineData("A1", "CellRef")] [InlineData("$a$1", "CellRef")] [InlineData("ZZ100000", "CellRef")]
    [InlineData("A0", "Name")] [InlineData("ZZZ1", "Name")] [InlineData("A100001", "Name")] [InlineData("Rate", "Name")] [InlineData("my.name_1", "Name")]
    [InlineData("true", "Bool")] [InlineData("FALSE", "Bool")]
    public void Tokenizer_WordTokenClassification(string src, string kind) { Assert.True(Tokenizer.TryTokenize(src, out var t)); Assert.Equal(Enum.Parse<TokenKind>(kind), t[0].Kind); }

    [Theory] [InlineData("$A0")] [InlineData("A$0")] [InlineData("$Foo")] [InlineData("\"abc")] [InlineData("#BOGUS!")] [InlineData("A1 ~ 2")] [InlineData("$SUM(1)")]
    public void Tokenizer_LexicalErrors(string src) => Assert.False(Tokenizer.TryTokenize(src, out _));

    [Fact] public void FunctionRequiresAdjacentParen() { Tokenizer.TryTokenize("SUM (A1)", out var t); Assert.Equal(TokenKind.Name, t[0].Kind); Assert.Equal(TokenKind.LParen, t[1].Kind); }
    [Fact] public void FunctionAdjacentParen() { Tokenizer.TryTokenize("sum(A1)", out var t); Assert.Equal(TokenKind.Function, t[0].Kind); Assert.Equal("sum", t[0].Text); Assert.Equal(TokenKind.LParen, t[1].Kind); }
    [Fact] public void StringDoubledQuote() { Tokenizer.TryTokenize("\"a\"\"b\"", out var t); Assert.Equal("a\"b", t[0].Text); }
    [Fact] public void NumberLexemeKept() { Tokenizer.TryTokenize("1.50+.5e3", out var t); Assert.Equal("1.50", t[0].Text); Assert.Equal(".5e3", t[2].Text); }
    [Fact] public void TwoCharOps() { Tokenizer.TryTokenize("1<=2<>3>=4", out var t); Assert.Equal(new[]{"<=","<>",">="}, t.Where(x=>x.Kind==TokenKind.Op).Select(x=>x.Text)); }
    [Fact] public void ErrorLiteral() { Tokenizer.TryTokenize("#DIV/0!+#N/A", out var t); Assert.Equal("#DIV/0!", t[0].Text); Assert.Equal("#N/A", t[2].Text); }
    [Fact] public void WhitespaceSkipped() { Assert.True(Tokenizer.TryTokenize(" \t1 + 2 ", out var t)); Assert.Equal(4, t.Count); }
    [Fact] public void EndsWithEndToken() { Tokenizer.TryTokenize("A1:B2", out var t); Assert.Equal(TokenKind.End, t[^1].Kind); Assert.Equal(new[]{"A1",":","B2",""}, t.Select(x=>x.Text)); }
}
