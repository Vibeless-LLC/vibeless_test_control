namespace Gridline.Core.Tests;

/// <summary>Phase 1 name storage and evaluation subset loaded through FromSnapshot (§8.2, §9.6, §6.7).</summary>
public class NamesTests
{
    private static KeyValuePair<string, string> KV(string k, string v) => KeyValuePair.Create(k, v);

    private static Workbook Load(KeyValuePair<string, string>[] names, params KeyValuePair<string, string>[] cells) =>
        Workbook.FromSnapshot(cells, names);

    private static string V(Workbook wb, string a) => wb.GetValue(a).ToCanonicalString();

    [Fact]
    public void GetNames_SortedOrdinalIgnoreCase_DefinedCasing_CanonicalRefersTo()
    {
        var wb = Load([KV("total", "$a$1:$a$10"), KV("Rate", "$B$1"), KV("_z", "$C$1"), KV("alpha", "$c$5:$a$1")]);
        Assert.Equal(
            // OrdinalIgnoreCase compares upper-cased code units: 'A' (0x41) < 'R' < 'T' < '_' (0x5F).
            new[] { KV("alpha", "$A$1:$C$5"), KV("Rate", "$B$1"), KV("total", "$A$1:$A$10"), KV("_z", "$C$1") },
            wb.GetNames());
    }

    [Fact]
    public void GetNames_EmptyByDefault() => Assert.Empty(new Workbook().GetNames());

    [Theory]
    [InlineData("TRUE", "$A$1")] [InlineData("A1", "$A$1")] [InlineData("1x", "$A$1")] [InlineData("", "$A$1")]
    [InlineData("Rate", "B1")] [InlineData("Rate", "$B$1:C2")] [InlineData("Rate", "")] [InlineData("Rate", "#NAME?")] [InlineData("Rate", "#ref!")]
    public void FromSnapshot_InvalidNameOrRefersTo_Throws(string name, string refersTo) =>
        Assert.Throws<ArgumentException>(() => Load([KV(name, refersTo)], KV("A1", "1")));

    [Fact]
    public void FromSnapshot_DuplicateNameCaseInsensitive_Throws() =>
        Assert.Throws<ArgumentException>(() => Load([KV("Rate", "$A$1"), KV("RATE", "$B$1")]));

    [Fact]
    public void FromSnapshot_NullName_Throws() =>
        Assert.ThrowsAny<ArgumentException>(() => Load([KV(null!, "$A$1")]));

    [Fact]
    public void CellName_BehavesLikeCellRef()
    {
        var wb = Load([KV("Rate", "$B$1")], KV("B1", "5"), KV("A1", "=Rate*2"), KV("A2", "=rate"), KV("A3", "=ISBLANK(Rate)"));
        Assert.Equal("N:10", V(wb, "A1"));
        Assert.Equal("N:5", V(wb, "A2"));
        Assert.Equal("B:FALSE", V(wb, "A3"));
    }

    [Fact]
    public void RangeName_BehavesLikeRange()
    {
        var wb = Load([KV("Total", "$A$1:$A$3")], KV("A1", "1"), KV("A2", "2"), KV("A3", "3"), KV("B1", "=SUM(Total)"), KV("B2", "=Total"), KV("B3", "=COUNT(TOTAL)"));
        Assert.Equal("N:6", V(wb, "B1"));
        Assert.Equal("E:#VALUE!", V(wb, "B2"));
        Assert.Equal("N:3", V(wb, "B3"));
    }

    [Fact]
    public void UndefinedName_IsNameError() =>
        Assert.Equal("E:#NAME?", V(Load([KV("Rate", "$B$1")], KV("A1", "=Nope+1")), "A1"));

    [Fact]
    public void RefErrorName_EvaluatesToRef_AndRoundTripsInGetNames()
    {
        var wb = Load([KV("Gone", "#REF!")], KV("A1", "=Gone"), KV("A2", "=SUM(Gone)"), KV("A3", "=ISERROR(Gone)"));
        Assert.Equal("E:#REF!", V(wb, "A1"));
        Assert.Equal("E:#REF!", V(wb, "A2"));
        Assert.Equal("B:TRUE", V(wb, "A3"));
        Assert.Equal(new[] { KV("Gone", "#REF!") }, wb.GetNames());
    }

    [Fact]
    public void EditingNameTarget_RecalcsDependents()
    {
        var wb = Load([KV("Rate", "$B$1"), KV("Total", "$A$1:$A$3")], KV("B1", "5"), KV("C1", "=Rate*2"), KV("C2", "=SUM(Total)"));
        var changed = new List<string>();
        wb.CellsChanged += (_, e) => changed.AddRange(e.Addresses);

        wb.SetCell("B1", "7");
        Assert.Equal("N:14", V(wb, "C1"));
        Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal(new[] { "B1", "C1" }, changed);

        wb.SetCell("A2", "4"); // a blank cell inside a range name's target
        Assert.Equal("N:4", V(wb, "C2"));
        Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount);
    }

    [Fact]
    public void FormulaAddedAfterLoad_ResolvesName()
    {
        var wb = Load([KV("Rate", "$B$1")], KV("B1", "3"));
        wb.SetCell("A1", "=Rate+1");
        Assert.Equal("N:4", V(wb, "A1"));
        wb.SetCell("B1", "10");
        Assert.Equal("N:11", V(wb, "A1"));
    }

    [Fact]
    public void CycleThroughName_IsCirc()
    {
        var wb = Load([KV("Me", "$A$1"), KV("Loop", "$B$1:$B$2")],
            KV("A1", "=Me+1"), KV("B1", "=SUM(Loop)"), KV("C1", "=A1"), KV("D1", "=IFERROR(B1,0)"));
        Assert.Equal("E:#CIRC!", V(wb, "A1"));
        Assert.Equal("E:#CIRC!", V(wb, "B1"));
        Assert.Equal("E:#CIRC!", V(wb, "C1"));
        Assert.Equal("N:0", V(wb, "D1"));
    }

    [Fact]
    public void CycleThroughName_TwoCells_IsCirc_AndBreaks()
    {
        var wb = Load([KV("X", "$B$1")], KV("A1", "=X"), KV("B1", "=A1"));
        Assert.Equal("E:#CIRC!", V(wb, "A1"));
        Assert.Equal("E:#CIRC!", V(wb, "B1"));
        wb.SetCell("B1", "2");
        Assert.Equal("N:2", V(wb, "A1"));
    }

    [Fact]
    public void SingleCellRangeRefersTo_IsCellName()
    {
        var wb = Load([KV("One", "$A$1:$A$1")], KV("A1", "4"), KV("B1", "=One*2"));
        Assert.Equal("N:8", V(wb, "B1"));
        Assert.Equal(new[] { KV("One", "$A$1") }, wb.GetNames());
    }

    [Fact]
    public void UnderscoreName_LoadsButIsNotLexable()
    {
        var wb = Load([KV("_x", "$A$1")], KV("A1", "4"), KV("B1", "=_x"));
        Assert.Equal("E:#PARSE!", V(wb, "B1")); // §5.1 word tokens start with a letter
        Assert.Equal(new[] { KV("_x", "$A$1") }, wb.GetNames());
    }

    [Fact]
    public void Load_CountsFormulas_NamesDoNotCount()
    {
        var wb = Load([KV("Rate", "$B$1")], KV("B1", "5"), KV("A1", "=Rate"));
        Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount);
    }
}
