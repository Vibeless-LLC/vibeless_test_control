namespace Gridline.Core.Tests;

public class CellValueTests
{
    [Fact] public void FromNumber_NegativeZeroNormalized() { var v = CellValue.FromNumber(-0.0); Assert.Equal(0.0, v.Number); Assert.False(double.IsNegative(v.Number)); Assert.Equal("N:0", v.ToCanonicalString()); }
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void FromNumber_NonFinite_IsNum(double d) => Assert.Equal("E:#NUM!", CellValue.FromNumber(d).ToCanonicalString());
    [Fact] public void FromError_Unknown_Throws() => Assert.Throws<ArgumentException>(() => CellValue.FromError("#BAD!"));
    [Fact] public void Canonical_Text_Escapes() => Assert.Equal("T:a\\\\b\\tc\\nd\\re", CellValue.FromText("a\\b\tc\nd\re").ToCanonicalString());
    [Fact] public void Canonical_Number_UsesR() => Assert.Equal("N:0.1", CellValue.FromNumber(0.1).ToCanonicalString());
    [Fact] public void Canonical_Others() { Assert.Equal("BLANK", CellValue.Blank.ToCanonicalString()); Assert.Equal("B:TRUE", CellValue.FromBoolean(true).ToCanonicalString()); Assert.Equal("E:#DIV/0!", CellValue.FromError(ErrorCodes.Div0).ToCanonicalString()); }
    [Fact] public void DefaultFields() { var t = CellValue.FromText("x"); Assert.Equal(0, t.Number); Assert.Equal("", t.Error); Assert.False(t.Boolean); Assert.Equal("", CellValue.Blank.Text); }
    [Fact] public void Equality() { Assert.Equal(CellValue.FromText("a"), CellValue.FromText("a")); Assert.NotEqual(CellValue.FromText("a"), CellValue.FromText("A")); }

    [Fact] public void Default_IsBlank() { var d = default(CellValue); Assert.Equal(ValueKind.Blank, d.Kind); Assert.Equal("", d.Text); Assert.Equal("", d.Error); Assert.Equal(CellValue.Blank, d); Assert.Equal("BLANK", d.ToString()); }
    [Fact] public void ErrorCodes_IsValid() { Assert.True(ErrorCodes.IsValid("#CIRC!")); Assert.False(ErrorCodes.IsValid("#div/0!")); Assert.False(ErrorCodes.IsValid("")); }
    [Fact] public void Equality_DifferentKinds() { Assert.NotEqual(CellValue.FromText(""), CellValue.Blank); Assert.Equal(CellValue.FromNumber(0).GetHashCode(), CellValue.FromNumber(-0.0).GetHashCode()); }
    [Fact] public void EventArgs_CarriesAddresses() => Assert.Equal(new[] { "A1" }, new CellsChangedEventArgs(new[] { "A1" }).Addresses);
}
