using Gridline.Core.Addressing;

namespace Gridline.Core.Tests.Addressing;

/// <summary>§9.6 name syntax and refersTo rules.</summary>
public class NameRulesTests
{
    [Theory]
    [InlineData("Rate")] [InlineData("_x")] [InlineData("a")] [InlineData("Tax.Rate_2")] [InlineData("ABCD1")] [InlineData("A1B")]
    [InlineData("A")] [InlineData("R1C1x")] [InlineData("TRUEX")] [InlineData("A.1")]
    public void IsValidName_Accepts(string name) => Assert.True(NameRules.IsValidName(name));

    [Fact]
    public void IsValidName_MaxLength255() => Assert.True(NameRules.IsValidName("N" + new string('x', 254)));

    [Theory]
    [InlineData("")] [InlineData("1abc")] [InlineData(".a")] [InlineData("a b")] [InlineData("a-b")] [InlineData("a$")] [InlineData("é")]
    [InlineData("TRUE")] [InlineData("false")] [InlineData("True")]
    [InlineData("A1")] [InlineData("zz100")] [InlineData("ABC123")] [InlineData("a0")] [InlineData("XFD1048576")]
    public void IsValidName_Rejects(string name) => Assert.False(NameRules.IsValidName(name));

    [Fact]
    public void IsValidName_RejectsNullAndTooLong()
    {
        Assert.False(NameRules.IsValidName(null!));
        Assert.False(NameRules.IsValidName("N" + new string('x', 255)));
    }

    [Theory]
    [InlineData("$B$1", "$B$1")] [InlineData("$a$1", "$A$1")] [InlineData("$A$1:$C$10", "$A$1:$C$10")] [InlineData("$c$10:$a$1", "$A$1:$C$10")]
    [InlineData("$ZZ$100000", "$ZZ$100000")] [InlineData("$A$1:$A$1", "$A$1")] [InlineData("$A$01", "$A$1")]
    public void TryParseRefersTo_CanonicalRoundTrip(string text, string canonical)
    {
        Assert.True(NameRules.TryParseRefersTo(text, out var r));
        Assert.Equal(canonical, NameRules.FormatRefersTo(r));
    }

    [Theory]
    [InlineData("")] [InlineData("B1")] [InlineData("$B1")] [InlineData("B$1")] [InlineData("$B$1:C3")] [InlineData("$A$0")] [InlineData("$AAA$1")]
    [InlineData("$A$100001")] [InlineData(" $A$1")] [InlineData("$A$1 ")] [InlineData("$A$1:")] [InlineData("$A$1:$B$2:$C$3")] [InlineData("#REF!")]
    [InlineData("=$A$1")] [InlineData("Rate")] [InlineData("$$A1")]
    public void TryParseRefersTo_Rejects(string text) => Assert.False(NameRules.TryParseRefersTo(text, out _));

    [Fact]
    public void FormatRefersTo_SingleCellAndRange()
    {
        Assert.Equal("$B$1", NameRules.FormatRefersTo(RangeAddress.Parse("B1")));
        Assert.Equal("$A$1:$A$10", NameRules.FormatRefersTo(RangeAddress.Parse("A10:A1")));
    }

    [Fact]
    public void FormatRefersTo_AsRange_KeepsOneByOneRangeForm()
    {
        Assert.Equal("$A$1:$A$1", NameRules.FormatRefersTo(RangeAddress.Parse("A1"), true));
        Assert.Equal("$A$1", NameRules.FormatRefersTo(RangeAddress.Parse("A1"), false));
        Assert.Equal("$A$1:$A$10", NameRules.FormatRefersTo(RangeAddress.Parse("A1:A10"), true));
    }
}
