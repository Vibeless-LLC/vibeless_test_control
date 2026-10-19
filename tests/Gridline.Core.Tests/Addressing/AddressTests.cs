using Gridline.Core.Addressing;

namespace Gridline.Core.Tests.Addressing;

public class AddressTests
{
    [Theory]
    [InlineData("a1", 1, 1)] [InlineData("$B$7", 7, 2)] [InlineData("zz100000", 100000, 702)] [InlineData("Az12", 12, 52)]
    [InlineData("A01", 1, 1)]
    public void CellAddress_Parses(string s, int row, int col) { Assert.True(CellAddress.TryParse(s, out var a)); Assert.Equal(new CellAddress(row, col), a); }

    [Theory]
    [InlineData("A0")] [InlineData("A100001")] [InlineData("AAA1")] [InlineData("")] [InlineData("1A")] [InlineData("A")] [InlineData("A1 ")] [InlineData("$$A1")] [InlineData("A$$1")]
    [InlineData("A99999999999")] [InlineData("A1B")] [InlineData("$")]
    public void CellAddress_RejectsInvalid(string s) => Assert.False(CellAddress.TryParse(s, out _));

    [Fact] public void CellAddress_CanonicalForm() => Assert.Equal("B7", CellAddress.Parse("$b$7").ToString());
    [Fact] public void CellAddress_ParseThrows() => Assert.Throws<ArgumentException>(() => CellAddress.Parse("A0"));
    [Fact] public void Range_Normalizes() => Assert.Equal("A1:B3", RangeAddress.Parse("B3:A1").ToString());
    [Fact] public void Range_NormalizesMixedCorners() => Assert.Equal("A1:B3", RangeAddress.Parse("A3:B1").ToString());
    [Fact] public void Range_SingleCell() { var r = RangeAddress.Parse("c4"); Assert.Equal("C4", r.ToString()); Assert.Equal(1, r.Rows); Assert.Equal(1, r.Cols); }
    [Fact] public void Range_DimensionsAndContains()
    {
        var r = RangeAddress.Parse("$A$1:B3");
        Assert.Equal(3, r.Rows); Assert.Equal(2, r.Cols);
        Assert.True(r.Contains(CellAddress.Parse("B2"))); Assert.False(r.Contains(CellAddress.Parse("C1")));
    }
    [Theory] [InlineData("A1:")] [InlineData(":A1")] [InlineData("A1:B2:C3")] [InlineData("A1:ZZZ1")] public void Range_RejectsInvalid(string s) => Assert.False(RangeAddress.TryParse(s, out _));
    [Fact] public void Key_SortsRowMajor() => Assert.True(CellAddress.Parse("ZZ1").Key < CellAddress.Parse("A2").Key);
    [Fact] public void Key_RoundTrips() { var a = CellAddress.Parse("ZZ100000"); Assert.Equal(a, CellAddress.FromKey(a.Key)); }
    [Fact] public void ColumnName_RoundTrips() { for (int c = 1; c <= 702; c++) { Assert.True(ColumnName.TryParse(ColumnName.ToLetters(c), out var back)); Assert.Equal(c, back); } }
    [Theory] [InlineData(1, "A")] [InlineData(26, "Z")] [InlineData(27, "AA")] [InlineData(702, "ZZ")] public void ColumnName_Letters(int c, string s) => Assert.Equal(s, ColumnName.ToLetters(c));
}
