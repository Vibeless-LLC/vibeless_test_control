using System.Text;

namespace Gridline.Core.Tests;

/// <summary>Insert and delete rows/columns with reference adjustment in formulas and names (§9.5, §9.6, §5.5, §9.3).</summary>
public class StructureEditTests
{
    [Fact] public void InsertRows_ShiftsCellsAndRefs() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A3","=SUM(A1:A2)+A1"); wb.InsertRows(2, 1); Assert.Equal("=SUM(A1:A3)+A1", wb.GetInput("A4")); Assert.Equal("", wb.GetInput("A3")); }
    [Fact] public void UnchangedFormula_KeepsVerbatimText() { var wb = new Workbook(); wb.SetCell("A5","= b1 + 1"); wb.InsertRows(3, 2); Assert.Equal("= b1 + 1", wb.GetInput("A7")); }
    [Fact] public void DeleteRows_RefToDeletedIsRef() { var wb = new Workbook(); wb.SetCell("A2","5"); wb.SetCell("B5","=A2*2"); wb.DeleteRows(2, 1); Assert.Equal("=#REF!*2", wb.GetInput("B4")); Assert.Equal("E:#REF!", wb.GetValue("B4").ToCanonicalString()); }
    [Fact] public void DeleteColumns_RangeShrinks() { var wb = new Workbook(); wb.SetCell("E1","=SUM($A$1:D1)"); wb.DeleteColumns(2, 2); Assert.Equal("=SUM($A$1:B1)", wb.GetInput("C1")); }
    [Fact] public void InsertColumns_LettersAndNames() { var wb = new Workbook(); wb.DefineName("R","$B$1:$C$2"); wb.InsertColumns(2, 1); Assert.Equal("$C$1:$D$2", wb.GetNames()[0].Value); }
    [Fact] public void Name_FullyDeleted_IsRef() { var wb = new Workbook(); wb.DefineName("R","$A$2:$A$3"); wb.SetCell("B1","=R"); wb.DeleteRows(2, 2); Assert.Equal("#REF!", wb.GetNames()[0].Value); Assert.Equal("E:#REF!", wb.GetValue("B1").ToCanonicalString()); }
    [Fact] public void InsertRows_DataOverflowThrows() { var wb = new Workbook(); wb.SetCell("A100000","x"); Assert.Throws<InvalidOperationException>(() => wb.InsertRows(1, 1)); Assert.Equal("x", wb.GetInput("A100000")); Assert.True(wb.Undo()); Assert.False(wb.CanUndo); }
    [Fact] public void InsertRows_RefOverflow() { var wb = new Workbook(); wb.SetCell("A1","=A99999+SUM(B99990:B100000)"); wb.InsertRows(2, 5); Assert.Equal("=#REF!+SUM(B99995:B100000)", wb.GetInput("A1")); }
    [Fact] public void ArgValidation() { var wb = new Workbook(); Assert.Throws<ArgumentOutOfRangeException>(() => wb.InsertRows(0, 1)); Assert.Throws<ArgumentOutOfRangeException>(() => wb.InsertRows(1, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => wb.DeleteRows(99999, 3)); Assert.Throws<ArgumentOutOfRangeException>(() => wb.DeleteColumns(703, 1)); }
    [Fact] public void DeleteRows_UndoRestoresVerbatimAndNames() { var wb = new Workbook(); wb.SetCell("A3","= a1 + 1"); wb.DefineName("R","$A$1"); wb.DeleteRows(1, 1); Assert.Equal("=#REF!+1", wb.GetInput("A2")); wb.Undo(); Assert.Equal("= a1 + 1", wb.GetInput("A3")); Assert.Equal("$A$1", wb.GetNames()[0].Value); }
    [Fact(Skip = "needs Task 4")] public void Row_ReflectsMove() { var wb = new Workbook(); wb.SetCell("A2","=ROW()"); wb.InsertRows(1, 3); Assert.Equal("N:5", wb.GetValue("A5").ToCanonicalString()); }

    // ---- additional coverage ----

    [Fact]
    public void ArgValidation_MoreEdges()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1");
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.InsertRows(100_001, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.InsertRows(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.DeleteRows(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.DeleteRows(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.DeleteRows(2, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.InsertColumns(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.InsertColumns(703, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.DeleteColumns(700, 4));
        Assert.Equal("1", wb.GetInput("A1"));
        wb.DeleteRows(100_000, 1); // last row: valid, no-op
        wb.InsertRows(100_000, int.MaxValue); // nothing at or below row 100000 moves: valid, no-op
        wb.DeleteColumns(702, 1);
        Assert.True(wb.Undo());
        Assert.False(wb.CanUndo); // none of the no-ops recorded a step
    }

    [Fact]
    public void InsertColumns_DataOverflowThrows_NothingChanges()
    {
        var wb = new Workbook();
        wb.SetCell("ZY1", "x"); wb.SetCell("A1", "=ZY1");
        Assert.Throws<InvalidOperationException>(() => wb.InsertColumns(1, 2));
        Assert.Equal("x", wb.GetInput("ZY1"));
        Assert.Equal("=ZY1", wb.GetInput("A1"));
        wb.InsertColumns(1, 1); // ZY → ZZ fits
        Assert.Equal("x", wb.GetInput("ZZ1"));
        Assert.Equal("=ZZ1", wb.GetInput("B1"));
        Assert.Equal("T:x", wb.GetValue("B1").ToCanonicalString());
    }

    [Fact]
    public void InsertRows_ValuesRecalculateAndRefsFollow()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "2"); wb.SetCell("A2", "3"); wb.SetCell("B1", "=A1*A2"); wb.SetCell("C5", "=SUM(A1:A2)");
        wb.InsertRows(2, 2);
        Assert.Equal("=A1*A4", wb.GetInput("B1"));
        Assert.Equal("=SUM(A1:A4)", wb.GetInput("C7"));
        Assert.Equal("N:6", wb.GetValue("B1").ToCanonicalString());
        Assert.Equal("N:5", wb.GetValue("C7").ToCanonicalString());
        wb.SetCell("A2", "10"); // inside the widened range
        Assert.Equal("N:15", wb.GetValue("C7").ToCanonicalString());
    }

    [Fact]
    public void ReversedRange_NormalizedWithPerComponentFlags()
    {
        var wb = new Workbook();
        wb.SetCell("D1", "=SUM($B3:A$1)");
        wb.InsertRows(2, 1);
        // Top-left takes row 1 (A$1: absolute) and column A (A$1: relative); bottom-right row 4 ($B3: relative), column B ($B3: absolute).
        Assert.Equal("=SUM(A$1:$B4)", wb.GetInput("D1"));
    }

    [Fact]
    public void ReversedRange_UnchangedKeepsVerbatim()
    {
        var wb = new Workbook();
        wb.SetCell("D1", "=sum( B3:A1 )");
        wb.InsertRows(10, 1);
        Assert.Equal("=sum( B3:A1 )", wb.GetInput("D1"));
        wb.InsertColumns(5, 1);
        Assert.Equal("=sum( B3:A1 )", wb.GetInput("D1"));
    }

    [Fact]
    public void OnlyEditAxisShifts_AbsoluteTreatedSame()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "=$C$3+C3+$C3+C$3");
        wb.InsertColumns(2, 1);
        Assert.Equal("=$D$3+D3+$D3+D$3", wb.GetInput("A1"));
        wb.InsertRows(5, 1); // below every reference
        Assert.Equal("=$D$3+D3+$D3+D$3", wb.GetInput("A1"));
    }

    [Fact]
    public void ChangedFormula_IsCanonical_NameCasingFromDefinition()
    {
        var wb = new Workbook();
        wb.DefineName("Rate", "$Z$1");
        wb.SetCell("A1", "= sum( a5 , 1.50 ) * rate & \"q\"\"\"");
        wb.InsertRows(2, 1);
        Assert.Equal("=SUM(A6,1.50)*Rate&\"q\"\"\"", wb.GetInput("A1"));
    }

    [Fact]
    public void ParseErrorFormula_MovedVerbatim()
    {
        var wb = new Workbook();
        wb.SetCell("A2", "=A1+(");
        wb.InsertRows(1, 1);
        Assert.Equal("=A1+(", wb.GetInput("A3"));
        Assert.Equal("E:#PARSE!", wb.GetValue("A3").ToCanonicalString());
    }

    [Fact]
    public void DeleteColumns_RefToDeletedIsRef_RangeFullyDeletedIsRef()
    {
        var wb = new Workbook();
        wb.SetCell("E1", "=B1+SUM(B1:C5)+D1");
        wb.DeleteColumns(2, 2);
        Assert.Equal("=#REF!+SUM(#REF!)+B1", wb.GetInput("C1"));
    }

    [Fact]
    public void NonFormulaInputs_MoveVerbatim()
    {
        var wb = new Workbook();
        wb.SetCell("B2", "'=A1"); wb.SetCell("B3", "1.50"); wb.SetCell("B4", "true");
        wb.DeleteRows(1, 1);
        Assert.Equal("'=A1", wb.GetInput("B1"));
        Assert.Equal("1.50", wb.GetInput("B2"));
        Assert.Equal("true", wb.GetInput("B3"));
        Assert.Equal(new[] { "B1", "B2", "B3" }, wb.GetUsedAddresses());
    }

    [Fact]
    public void Names_CellAndOneByOneRangeKeepForm()
    {
        var wb = new Workbook();
        wb.DefineName("C", "$B$2"); wb.DefineName("R", "$B$2:$B$2"); wb.DefineName("S", "$A$1:$A$5");
        wb.SetCell("B2", "7"); wb.SetCell("E1", "=C"); wb.SetCell("F1", "=SUM(R)");
        wb.InsertRows(2, 1);
        Assert.Equal(new[] { KV("C", "$B$3"), KV("R", "$B$3:$B$3"), KV("S", "$A$1:$A$6") }, wb.GetNames());
        Assert.Equal("N:7", wb.GetValue("E1").ToCanonicalString());
        wb.DeleteRows(3, 1);
        Assert.Equal(new[] { KV("C", "#REF!"), KV("R", "#REF!"), KV("S", "$A$1:$A$5") }, wb.GetNames());
        Assert.Equal("E:#REF!", wb.GetValue("E1").ToCanonicalString());
        Assert.Equal("E:#REF!", wb.GetValue("F1").ToCanonicalString());
        wb.Undo();
        Assert.Equal(new[] { KV("C", "$B$3"), KV("R", "$B$3:$B$3"), KV("S", "$A$1:$A$6") }, wb.GetNames());
        Assert.Equal("N:7", wb.GetValue("F1").ToCanonicalString());
    }

    [Fact]
    public void Names_ValuesFollowShiftedTarget()
    {
        var wb = new Workbook();
        wb.SetCell("B1", "4"); wb.DefineName("Rate", "$B$1"); wb.SetCell("A5", "=Rate*2");
        wb.InsertColumns(1, 1);
        Assert.Equal("$C$1", wb.GetNames()[0].Value);
        Assert.Equal("=Rate*2", wb.GetInput("B5"));
        Assert.Equal("N:8", wb.GetValue("B5").ToCanonicalString());
        wb.SetCell("C1", "5");
        Assert.Equal("N:10", wb.GetValue("B5").ToCanonicalString());
    }

    [Fact]
    public void OneStep_UndoRedo_EventOnce()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1"); wb.SetCell("A2", "=A1+1"); wb.DefineName("N", "$A$2");
        wb.MarkSaved();
        int events = 0;
        IReadOnlyCollection<string>? addrs = null;
        wb.CellsChanged += (_, e) => { events++; addrs = e.Addresses; };
        wb.InsertRows(1, 1);
        Assert.Equal(1, events);
        Assert.Equal(new[] { "A1", "A2", "A3" }, addrs);
        Assert.True(wb.IsDirty);
        Assert.Equal("=A2+1", wb.GetInput("A3"));
        Assert.Equal("$A$3", wb.GetNames()[0].Value);
        Assert.True(wb.Undo());
        Assert.False(wb.IsDirty);
        Assert.Equal(new[] { "A1", "A2" }, wb.GetUsedAddresses());
        Assert.Equal("=A1+1", wb.GetInput("A2"));
        Assert.Equal("$A$2", wb.GetNames()[0].Value);
        Assert.True(wb.Redo());
        Assert.Equal("=A2+1", wb.GetInput("A3"));
        Assert.Equal("N:2", wb.GetValue("A3").ToCanonicalString());
        Assert.Equal("$A$3", wb.GetNames()[0].Value);
    }

    [Fact]
    public void NoOp_NoStepNoEvent()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1");
        int events = 0;
        wb.CellsChanged += (_, _) => events++;
        wb.InsertRows(2, 3);
        wb.DeleteRows(5, 10);
        wb.InsertColumns(2, 1);
        Assert.Equal(0, events);
        Assert.True(wb.Undo());
        Assert.False(wb.CanUndo);
    }

    [Fact]
    public void ReemittedFormulaTooLong_Throws_NothingChanges()
    {
        var sb = new StringBuilder("=A9");
        while (sb.Length + 3 <= Workbook.MaxInputLength) sb.Append("+A9");
        string f = sb.ToString();
        var wb = new Workbook();
        wb.SetCell("B1", f);
        wb.SetCell("A9", "1");
        Assert.Throws<InvalidOperationException>(() => wb.InsertRows(1, 1));
        Assert.Equal(f, wb.GetInput("B1"));
        Assert.Equal("1", wb.GetInput("A9"));
        Assert.Equal("", wb.GetInput("A10"));
    }

    [Fact]
    public void DeleteRows_LargeMiddleSpan()
    {
        var wb = new Workbook();
        for (int i = 1; i <= 10; i++) wb.SetCell("A" + i, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        wb.SetCell("B1", "=SUM(A1:A10)");
        wb.DeleteRows(3, 5); // removes 3..7
        Assert.Equal(new[] { "A1", "B1", "A2", "A3", "A4", "A5" }, wb.GetUsedAddresses());
        Assert.Equal("=SUM(A1:A5)", wb.GetInput("B1"));
        Assert.Equal("N:30", wb.GetValue("B1").ToCanonicalString()); // 1+2+8+9+10
        wb.Undo();
        Assert.Equal("N:55", wb.GetValue("B1").ToCanonicalString());
        Assert.Equal(11, wb.GetUsedAddresses().Count);
    }

    [Fact]
    public void DeepNestingAndLongChain_Rewritten()
    {
        string nested = "=" + new string('(', 200) + "A5" + new string(')', 200);
        string chain = "=" + string.Join("+", Enumerable.Repeat("A5", 5000));
        var wb = new Workbook();
        wb.SetCell("B1", nested); wb.SetCell("C1", chain); wb.SetCell("A5", "2");
        wb.InsertRows(1, 1);
        Assert.Equal("=" + new string('(', 200) + "A6" + new string(')', 200), wb.GetInput("B2"));
        Assert.Equal("=" + string.Join("+", Enumerable.Repeat("A6", 5000)), wb.GetInput("C2"));
        Assert.Equal("N:10000", wb.GetValue("C2").ToCanonicalString());
    }

    private static KeyValuePair<string, string> KV(string k, string v) => KeyValuePair.Create(k, v);
}

/// <summary>Insert/delete near the top of a 100k-cell workbook (§9.5, §12). Serial in the Perf collection.</summary>
[Collection(Gridline.Tests.Shared.PerfCollection.Name)]
[Trait("Category", "Perf")]
public class StructureEditPerfTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void InsertDelete_100kCellsWithFormulas_Under2s()
    {
        var wb = Workbook.FromSnapshot(Gridline.Tests.Shared.PerfWorkloads.P1Cells(), []);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        wb.InsertRows(1, 1);
        long ins = sw.ElapsedMilliseconds;
        Assert.Equal("=A2+A3*2", wb.GetInput("B2"));
        sw.Restart();
        wb.DeleteRows(1, 1);
        long del = sw.ElapsedMilliseconds;
        Assert.Equal("=A1+A2*2", wb.GetInput("B1"));
        sw.Restart();
        wb.Undo();
        long undo = sw.ElapsedMilliseconds;
        output.WriteLine($"insert {ins} ms, delete {del} ms, undo {undo} ms");
        Assert.True(ins < 2000, $"insert took {ins} ms");
        Assert.True(del < 2000, $"delete took {del} ms");
        Assert.True(undo < 2000, $"undo took {undo} ms");
    }

    [Fact]
    public void DeleteUpperHalf_SingleColumn100k_Under2s()
    {
        var cells = new List<KeyValuePair<string, string>>(100_000);
        for (int i = 1; i <= 100_000; i++) cells.Add(new("A" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var wb = Workbook.FromSnapshot(cells, []);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        wb.DeleteRows(1, 50_000);
        long del = sw.ElapsedMilliseconds;
        Assert.Equal("50001", wb.GetInput("A1"));
        Assert.Equal(50_000, wb.GetUsedAddresses().Count);
        sw.Restart();
        wb.Undo();
        long undo = sw.ElapsedMilliseconds;
        Assert.Equal("1", wb.GetInput("A1"));
        Assert.Equal(100_000, wb.GetUsedAddresses().Count);
        output.WriteLine($"delete {del} ms, undo {undo} ms");
        Assert.True(del < 2000, $"delete took {del} ms");
        Assert.True(undo < 2000, $"undo took {undo} ms");
    }
}
