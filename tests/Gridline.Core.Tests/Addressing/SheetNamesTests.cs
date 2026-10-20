using Gridline.Core.Addressing;

namespace Gridline.Core.Tests.Addressing;

/// <summary>CR1.1 sheet-name validity and CR1.2 quoting / qualified addresses.</summary>
public class SheetNamesTests
{
    [Theory]
    [InlineData("Sheet1", true)] [InlineData("My Sheet", true)] [InlineData("", false)]
    [InlineData("abcdefghijabcdefghijabcdefghij1", true)]   // 31
    [InlineData("abcdefghijabcdefghijabcdefghij12", false)] // 32
    [InlineData("a:b", false)] [InlineData("a\\b", false)] [InlineData("a/b", false)] [InlineData("a?b", false)]
    [InlineData("a*b", false)] [InlineData("a[b", false)] [InlineData("a]b", false)]
    [InlineData("'ab", false)] [InlineData("ab'", false)] [InlineData("a'b", true)]
    public void IsValid(string name, bool ok) => Assert.Equal(ok, SheetNames.IsValid(name));

    [Fact] public void IsValid_Null() => Assert.False(SheetNames.IsValid(null));

    [Theory]
    [InlineData("Sheet1", "Sheet1")] [InlineData("My Sheet", "'My Sheet'")] [InlineData("2024", "'2024'")]
    [InlineData("A1", "'A1'")] [InlineData("xfd99", "'xfd99'")] [InlineData("ABCD1", "ABCD1")]
    [InlineData("true", "'true'")] [InlineData("It's", "'It''s'")] [InlineData("_x.y", "_x.y")]
    public void Format_QuotesOnlyWhenRequired(string name, string expected) => Assert.Equal(expected, SheetNames.Format(name));

    [Fact] public void NeedsQuoting_EmptyIsTrue() => Assert.True(SheetNames.NeedsQuoting(""));

    [Fact] public void Limits() { Assert.Equal(31, SheetNames.MaxLength); Assert.Equal(255, SheetNames.MaxSheets); }

    [Fact] public void QualifiedAddress_Parses()
    {
        Assert.True(QualifiedAddress.TryParse("'It''s'!b2:a1", out var q));
        Assert.Equal("It's", q.Sheet); Assert.Equal("A1:B2", q.Range.ToString()); Assert.True(q.IsRange);
        Assert.True(QualifiedAddress.TryParse("a1", out q)); Assert.Null(q.Sheet);
        Assert.False(QualifiedAddress.TryParse("Sheet2!A1:Sheet2!B3", out _));
        Assert.False(QualifiedAddress.TryParse("My Sheet!A1", out _));   // needs quotes
        Assert.False(QualifiedAddress.TryParse("A1!B2", out _));         // looks like a ref → needs quotes
        Assert.False(QualifiedAddress.TryParse("''!A1", out _));         // empty name
    }

    [Fact] public void QualifiedAddress_SingleCellAndNull()
    {
        Assert.True(QualifiedAddress.TryParse("Data!B2", out var q));
        Assert.Equal("Data", q.Sheet); Assert.False(q.IsRange); Assert.Equal("B2", q.Range.ToString());
        Assert.True(QualifiedAddress.TryParse("B2:B2", out q)); Assert.True(q.IsRange);
        Assert.False(QualifiedAddress.TryParse(null, out _));
        Assert.False(QualifiedAddress.TryParse("Data!", out _));
    }

    [Fact] public void TryParsePrefix_Variants()
    {
        Assert.True(SheetNames.TryParsePrefix("'a b'!C3", 0, out var n, out int after));
        Assert.Equal("a b", n); Assert.Equal(6, after);
        Assert.True(SheetNames.TryParsePrefix("=x_1.y!C3", 1, out n, out after));
        Assert.Equal("x_1.y", n); Assert.Equal(7, after);
        Assert.False(SheetNames.TryParsePrefix("'ab", 0, out _, out _));
        Assert.False(SheetNames.TryParsePrefix("TRUE!A1", 0, out _, out _));
        Assert.False(SheetNames.TryParsePrefix("!A1", 0, out _, out _));
        Assert.False(SheetNames.TryParsePrefix("abc", 0, out _, out _));
    }

    [Theory]
    [InlineData("Data", "Data", true)] [InlineData("'My Sheet'", "My Sheet", true)] [InlineData("'It''s'", "It's", true)]
    [InlineData("My Sheet", null, false)] [InlineData("A1", null, false)] [InlineData("''", null, false)] [InlineData("", null, false)]
    [InlineData("'a'b", null, false)]
    public void TryParseName(string token, string? expected, bool ok)
    {
        Assert.Equal(ok, SheetNames.TryParseName(token, out var name));
        if (ok) Assert.Equal(expected, name);
    }

    [Fact] public void TryParseCell()
    {
        Assert.True(QualifiedAddress.TryParseCell("'My Data'!c3", out var sheet, out var cell));
        Assert.Equal("My Data", sheet); Assert.Equal(new CellAddress(3, 3), cell);
        Assert.True(QualifiedAddress.TryParseCell("B2", out sheet, out cell)); Assert.Null(sheet);
        Assert.False(QualifiedAddress.TryParseCell("A1:B2", out _, out _));
        Assert.False(QualifiedAddress.TryParseCell("S!A0", out _, out _));
    }

    [Fact] public void TryParseColumnOrRow()
    {
        Assert.True(QualifiedAddress.TryParseColumnOrRow("'My Data'!C", true, out var sheet, out int i));
        Assert.Equal("My Data", sheet); Assert.Equal(3, i);
        Assert.True(QualifiedAddress.TryParseColumnOrRow("zz", true, out sheet, out i)); Assert.Null(sheet); Assert.Equal(702, i);
        Assert.False(QualifiedAddress.TryParseColumnOrRow("AAA", true, out _, out _));
        Assert.False(QualifiedAddress.TryParseColumnOrRow("5", true, out _, out _));
        Assert.True(QualifiedAddress.TryParseColumnOrRow("Data!5", false, out sheet, out i)); Assert.Equal("Data", sheet); Assert.Equal(5, i);
        Assert.True(QualifiedAddress.TryParseColumnOrRow("2147483647", false, out _, out i)); Assert.Equal(int.MaxValue, i);
        Assert.False(QualifiedAddress.TryParseColumnOrRow("2147483648", false, out _, out _));
        Assert.False(QualifiedAddress.TryParseColumnOrRow("0", false, out _, out _));
        Assert.False(QualifiedAddress.TryParseColumnOrRow("C", false, out _, out _));
    }
}
