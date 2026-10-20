using Gridline.IO;

namespace Gridline.IO.Tests;

public class DelimitedReaderTests
{
    [Fact] public void MixedLineEndings_NoTrailingRow() => Assert.Equal(3, DelimitedReader.Parse("a\r\nb\nc\r", ',').Count);
    [Fact] public void QuotedFieldWithDelimiterQuoteNewline() => Assert.Equal("x,\"y\"\nz", DelimitedReader.Parse("\"x,\"\"y\"\"\nz\",2", ',')[0][0]);
    [Fact] public void BareQuoteLiteral() => Assert.Equal("a\"b", DelimitedReader.Parse("a\"b", ',')[0][0]);
    [Fact] public void Unterminated_Throws() => Assert.Throws<GridlineFormatException>(() => DelimitedReader.Parse("\"abc", ','));
    [Fact] public void EmptyFields() => Assert.Equal(new[] { "", "", "" }, DelimitedReader.Parse(",,", ',')[0]);
    [Fact] public void EmptyText_NoRecords() => Assert.Empty(DelimitedReader.Parse("", ','));
    [Fact] public void BlankLine_IsOneEmptyField() { var r = DelimitedReader.Parse("a\n\nb", ','); Assert.Equal(3, r.Count); Assert.Equal(new[] { "" }, r[1]); }
    [Fact] public void TrailingDelimiterBeforeTerminator() => Assert.Equal(new[] { "a", "" }, DelimitedReader.Parse("a,\n", ',')[0]);
    [Fact] public void TabDelimiter() => Assert.Equal(new[] { "1", "2" }, DelimitedReader.Parse("1\t2\r\n", '\t')[0]);
    [Fact] public void QuotedEmptyField() => Assert.Equal(new[] { "", "b" }, DelimitedReader.Parse("\"\",b", ',')[0]);
    [Fact] public void TooManyFields_Throws() => Assert.Throws<GridlineFormatException>(() => DelimitedReader.Parse(new string(',', 702), ','));
    [Fact] public void MaxFields_Ok() => Assert.Equal(702, DelimitedReader.Parse(new string(',', 701), ',')[0].Count);
    [Fact] public void TooManyRecords_Throws() => Assert.Throws<GridlineFormatException>(() => DelimitedReader.Parse(string.Concat(Enumerable.Repeat("a\n", 100_001)), ','));
    [Theory] [InlineData("=1+1", "'=1+1")] [InlineData("'x", "''x")] [InlineData("-5", "-5")] [InlineData("", "")]
    public void Mapping(string f, string input) => Assert.Equal(input, DelimitedMapping.ToInput(f));
}
