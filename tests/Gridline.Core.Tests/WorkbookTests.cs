using Gridline.Core.Addressing;

namespace Gridline.Core.Tests;

public class WorkbookTests
{
    [Theory]
    [InlineData("5", "N:5")] [InlineData(" 5", "T: 5")] [InlineData("'=1", "T:=1")] [InlineData("true", "B:TRUE")] [InlineData("50%", "N:0.5")] [InlineData("1e999", "T:1e999")] [InlineData("1,000", "T:1,000")] [InlineData("=", "E:#PARSE!")] [InlineData("-0", "N:0")]
    public void InputInterpretation(string input, string value) { var wb = new Workbook(); wb.SetCell("A1", input); Assert.Equal(value, wb.GetValue("A1").ToCanonicalString()); Assert.Equal(input, wb.GetInput("a1")); }

    [Fact] public void EmptyInput_RemovesCell() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A1",""); Assert.Empty(wb.GetUsedAddresses()); Assert.Equal(ValueKind.Blank, wb.GetValue("A1").Kind); }
    [Fact] public void UsedAddresses_RowMajor() { var wb = new Workbook(); wb.SetCell("B2","1"); wb.SetCell("ZZ1","1"); wb.SetCell("A2","1"); Assert.Equal(new[]{"ZZ1","A2","B2"}, wb.GetUsedAddresses()); }
    [Fact] public void Recalc_Incremental() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("B1","=A1+1"); wb.SetCell("C1","=B1*2"); wb.SetCell("D1","=5"); wb.SetCell("A1","10"); Assert.Equal("N:22", wb.GetValue("C1").ToCanonicalString()); Assert.Equal(2, wb.LastRecalc.EvaluatedFormulaCount); wb.SetCell("Z9","3"); Assert.Equal(0, wb.LastRecalc.EvaluatedFormulaCount); }
    [Fact] public void BlankInRangeTriggersDependents() { var wb = new Workbook(); wb.SetCell("B1","=SUM(A1:A3)"); wb.SetCell("A2","4"); Assert.Equal("N:4", wb.GetValue("B1").ToCanonicalString()); }
    [Fact] public void Cycle_AllMembersCirc_DependentsGetErrorValue() { var wb = new Workbook(); wb.SetCell("A1","=B1"); wb.SetCell("B1","=A1"); wb.SetCell("C1","=IFERROR(A1,0)"); Assert.Equal("E:#CIRC!", wb.GetValue("A1").ToCanonicalString()); Assert.Equal("E:#CIRC!", wb.GetValue("B1").ToCanonicalString()); Assert.Equal("N:0", wb.GetValue("C1").ToCanonicalString()); }
    [Fact] public void Cycle_UntakenBranchStillCycle() { var wb = new Workbook(); wb.SetCell("A1","=IF(TRUE,1,A1)"); Assert.Equal("E:#CIRC!", wb.GetValue("A1").ToCanonicalString()); }
    [Fact] public void Workbook_BreakingCycle_RestoresAllMembersAndDependents() { var wb = new Workbook(); wb.SetCell("A1","=B1"); wb.SetCell("B1","=A1"); wb.SetCell("C1","=A1+1"); wb.SetCell("B1","5"); Assert.Equal("N:5", wb.GetValue("A1").ToCanonicalString()); Assert.Equal("N:6", wb.GetValue("C1").ToCanonicalString()); }
    [Fact] public void Workbook_InvalidAddress_ThrowsAndChangesNothing() { var wb = new Workbook(); wb.SetCell("A1","1"); int events = 0; wb.CellsChanged += (_, _) => events++; Assert.Throws<ArgumentException>(() => wb.SetCell("ZZZ1","x")); Assert.Throws<ArgumentException>(() => wb.GetValue("A0")); Assert.Throws<ArgumentException>(() => wb.GetInput("")); Assert.Throws<ArgumentException>(() => wb.SetCell("A1", new string('x', 32768))); Assert.Equal(0, events); Assert.Equal("1", wb.GetInput("A1")); }
    [Fact] public void CellsChanged_OncePerCall_IncludesDependents() { var wb = new Workbook(); wb.SetCell("B1","=A1"); IReadOnlyCollection<string>? got = null; int n = 0; wb.CellsChanged += (_, e) => { n++; got = e.Addresses; }; wb.SetCell("A1","2"); Assert.Equal(1, n); Assert.Equal(new[]{"A1","B1"}, got); n = 0; wb.SetCell("A1","2"); Assert.Equal(0, n); }
    [Fact] public void Eval_WordTokenEdgeCases() { var wb = new Workbook(); wb.SetCell("A1","=A0"); wb.SetCell("A2","=$A0"); wb.SetCell("A3","=SUM (B1)"); wb.SetCell("A4","=sum(b1)"); Assert.Equal("E:#NAME?", wb.GetValue("A1").ToCanonicalString()); Assert.Equal("E:#PARSE!", wb.GetValue("A2").ToCanonicalString()); Assert.Equal("E:#PARSE!", wb.GetValue("A3").ToCanonicalString()); Assert.Equal("N:0", wb.GetValue("A4").ToCanonicalString()); }
    [Fact] public void FromSnapshot_FullRecalc() { var wb = Workbook.FromSnapshot(new[]{ KeyValuePair.Create("B1","=A1*2"), KeyValuePair.Create("A1","3") }, []); Assert.Equal("N:6", wb.GetValue("B1").ToCanonicalString()); Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount); }
    [Fact] public void FromSnapshot_DuplicateCaseInsensitive_Throws() => Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot(new[]{ KeyValuePair.Create("a1","1"), KeyValuePair.Create("A1","2") }, []));

    // ---- additional coverage ----

    [Fact]
    public void FromSnapshot_InvalidInputs_Throw()
    {
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot(new[] { KeyValuePair.Create("A0", "1") }, []));
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot(new[] { KeyValuePair.Create("A1", new string('x', 32768)) }, []));
        // refersTo must be fully absolute (§9.6).
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot([], new[] { KeyValuePair.Create("Rate", "A1") }));
    }

    [Fact]
    public void FromSnapshot_CountsAllFormulas_IncludingCycles_NoEvent()
    {
        var wb = Workbook.FromSnapshot(new[] { KeyValuePair.Create("A1", "=B1"), KeyValuePair.Create("B1", "=A1"), KeyValuePair.Create("C1", "=A1"), KeyValuePair.Create("D1", "x") }, []);
        Assert.Equal(3, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal("E:#CIRC!", wb.GetValue("B1").ToCanonicalString());
        Assert.Equal("E:#CIRC!", wb.GetValue("C1").ToCanonicalString());
        Assert.Equal(new[] { "A1", "B1", "C1", "D1" }, wb.GetUsedAddresses());
        Assert.Empty(wb.GetNames());
    }

    [Fact]
    public void FormulaBecomesConstant_DropsItsDependencies()
    {
        var wb = new Workbook();
        wb.SetCell("B1", "=A1");
        wb.SetCell("B1", "7");
        wb.SetCell("A1", "1");
        Assert.Equal(0, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal("N:7", wb.GetValue("B1").ToCanonicalString());
    }

    [Fact]
    public void ClearingReferencedCell_RecalcsDependents_EventListsBoth()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "3");
        wb.SetCell("B1", "=A1*2");
        IReadOnlyCollection<string>? got = null;
        wb.CellsChanged += (_, e) => got = e.Addresses;
        wb.SetCell("A1", "");
        Assert.Equal("N:0", wb.GetValue("B1").ToCanonicalString());
        Assert.Equal(new[] { "A1", "B1" }, got);
        Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount);
    }

    [Fact]
    public void Event_OnlyValueChangedDependentsListed()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1");
        wb.SetCell("B1", "=A1>0");
        wb.SetCell("C1", "=A1");
        IReadOnlyCollection<string>? got = null;
        wb.CellsChanged += (_, e) => got = e.Addresses;
        wb.SetCell("A1", "2");
        Assert.Equal(new[] { "A1", "C1" }, got);
        Assert.Equal(2, wb.LastRecalc.EvaluatedFormulaCount);
    }

    [Fact]
    public void SettingSameFormula_ReevaluatesButRaisesNoEvent()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "=1+1");
        int n = 0;
        wb.CellsChanged += (_, _) => n++;
        wb.SetCell("A1", "=1+1");
        Assert.Equal(0, n);
        Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount);
    }

    [Fact]
    public void GetInput_ReturnsVerbatim_EmptyForBlank()
    {
        var wb = new Workbook();
        wb.SetCell("$b$2", "=sum( a1 )");
        Assert.Equal("=sum( a1 )", wb.GetInput("B2"));
        Assert.Equal("", wb.GetInput("C3"));
        Assert.Equal(new[] { "B2" }, wb.GetUsedAddresses());
    }

    [Fact]
    public void RangeCycle_Detected()
    {
        var wb = new Workbook();
        wb.SetCell("A3", "=SUM(A1:A5)");
        Assert.Equal("E:#CIRC!", wb.GetValue("A3").ToCanonicalString());
        wb.SetCell("A3", "=SUM(A1:A2)");
        Assert.Equal("N:0", wb.GetValue("A3").ToCanonicalString());
    }

    [Fact]
    public void LongChain_100k_NoStackOverflow()
    {
        var cells = new List<KeyValuePair<string, string>> { KeyValuePair.Create("A1", "1") };
        for (int r = 2; r <= 100_000; r++) cells.Add(KeyValuePair.Create("A" + r, "=A" + (r - 1) + "+1"));
        var wb = Workbook.FromSnapshot(cells, []);
        Assert.Equal("N:100000", wb.GetValue("A100000").ToCanonicalString());
        wb.SetCell("A1", "2");
        Assert.Equal("N:100001", wb.GetValue("A100000").ToCanonicalString());
        Assert.Equal(99_999, wb.LastRecalc.EvaluatedFormulaCount);
    }

    [Fact]
    public void SumOverWideSparseRange_RowMajorAndOnlyOccupied()
    {
        var wb = new Workbook();
        wb.SetCell("ZZ1", "'b");
        wb.SetCell("A2", "'c");
        wb.SetCell("C1", "'a");
        wb.SetCell("B5", "=CONCAT(A1:ZZ3)");
        Assert.Equal("T:abc", wb.GetValue("B5").ToCanonicalString());
        wb.SetCell("D2", "'d");
        Assert.Equal("T:abcd", wb.GetValue("B5").ToCanonicalString());
    }

    [Fact]
    public void CellStore_KeysInRange_RowMajor()
    {
        var store = new CellStore();
        string[] addrs = ["C3", "A1", "B2", "ZZ2", "A3", "B100", "D1"];
        foreach (var a in addrs) store.Set(CellAddress.Parse(a).Key, new Cell("1", CellKind.Number, null, CellValue.FromNumber(1), null));
        string Keys(string range) => string.Join(",", store.KeysInRange(RangeAddress.Parse(range)).Select(k => CellAddress.FromKey(k).ToString()));
        Assert.Equal("A1,B2,A3,C3", Keys("A1:C3"));
        Assert.Equal("D1,B2,ZZ2,C3,B100", Keys("B1:ZZ100000"));
        Assert.Equal("B2,B100", Keys("B1:B100"));
        Assert.Equal("", Keys("E5:F9"));
        Assert.Equal("A1,D1,B2,ZZ2,A3,C3,B100", string.Join(",", store.KeysRowMajor().Select(k => CellAddress.FromKey(k).ToString())));
        store.Remove(CellAddress.Parse("B2").Key);
        Assert.Equal("A1,A3,C3", Keys("A1:C3"));
        Assert.False(store.TryGet(CellAddress.Parse("B2").Key, out _));
    }

    [Fact]
    public void SingleColumnRange_TracksOutOfOrderInsertOverwriteAndRemove()
    {
        var wb = new Workbook();
        wb.SetCell("B1", "=SUM(A1:A200)");
        wb.SetCell("A150", "5"); wb.SetCell("A3", "2"); wb.SetCell("A100", "7"); wb.SetCell("A201", "1000");
        Assert.Equal("N:14", wb.GetValue("B1").ToCanonicalString());
        wb.SetCell("A3", "10");
        Assert.Equal("N:22", wb.GetValue("B1").ToCanonicalString());
        wb.SetCell("A100", "");
        Assert.Equal("N:15", wb.GetValue("B1").ToCanonicalString());
        wb.SetCell("A150", "=A3*2");
        Assert.Equal("N:30", wb.GetValue("B1").ToCanonicalString());
        Assert.Equal("N:-3", Eval("=MIN(A1:A5)", ("A1", "-1"), ("A2", "-3"), ("A4", "2")));
        Assert.Equal("N:2", Eval("=MAX(A1:A5)", ("A1", "-1"), ("A2", "-3"), ("A4", "2")));
    }

    private static string Eval(string f, params (string, string)[] inputs) => CoreTestHost.Eval(f, inputs);
}
