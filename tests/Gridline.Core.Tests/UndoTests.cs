using Gridline.Core.Commands;

namespace Gridline.Core.Tests;

/// <summary>Undo/redo, ApplyBatch and dirty tracking (§9.3).</summary>
public class UndoTests
{
    private static KeyValuePair<string, string> KV(string k, string v) => KeyValuePair.Create(k, v);

    // ---- Brief tests (verbatim) ----
    [Fact] public void Undo_RestoresVerbatimFormula() { var wb = new Workbook(); wb.SetCell("A1","= 1 +  2"); wb.SetCell("A1","5"); Assert.True(wb.Undo()); Assert.Equal("= 1 +  2", wb.GetInput("A1")); Assert.True(wb.Redo()); Assert.Equal("5", wb.GetInput("A1")); }
    [Fact] public void Undo_Nothing_False() { var wb = new Workbook(); Assert.False(wb.Undo()); Assert.False(wb.Redo()); Assert.False(wb.CanUndo); }
    [Fact] public void SameInput_NoStep() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A1","1"); wb.Undo(); Assert.False(wb.CanUndo); }
    [Fact] public void NewMutation_ClearsRedo() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.Undo(); wb.SetCell("B1","2"); Assert.False(wb.CanRedo); }
    [Fact] public void Limit100_DropsOldest() { var wb = new Workbook(); for (int i = 1; i <= 101; i++) wb.SetCell("A1", i.ToString()); int n = 0; while (wb.Undo()) n++; Assert.Equal(100, n); Assert.Equal("1", wb.GetInput("A1")); }
    [Fact] public void Batch_OneStep_LastWins_Atomic() { var wb = new Workbook(); wb.ApplyBatch([new("A1","1"), new("B1","2"), new("a1","3")]); Assert.Equal("3", wb.GetInput("A1")); wb.Undo(); Assert.Empty(wb.GetUsedAddresses()); Assert.Throws<ArgumentException>(() => wb.ApplyBatch([new("A1","9"), new("A0","1")])); Assert.Empty(wb.GetUsedAddresses()); }
    [Fact] public void Batch_Empty_NoStep() { var wb = new Workbook(); wb.ApplyBatch([]); Assert.False(wb.CanUndo); Assert.False(wb.IsDirty); }
    [Fact] public void Dirty_UndoToSavedIsClean() { var wb = new Workbook(); Assert.False(wb.IsDirty); wb.SetCell("A1","1"); Assert.True(wb.IsDirty); wb.MarkSaved(); wb.SetCell("A1","2"); wb.Undo(); Assert.False(wb.IsDirty); wb.Undo(); Assert.True(wb.IsDirty); }
    [Fact] public void Dirty_SavedPositionDiscardedByNewBranch() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.MarkSaved(); wb.Undo(); wb.SetCell("A1","2"); wb.Undo(); Assert.True(wb.IsDirty); }
    [Fact] public void Dirty_SavedPositionDropped_StaysDirty() { var wb = new Workbook(); wb.MarkSaved(); for (int i = 0; i < 101; i++) wb.SetCell("A1", i.ToString()); while (wb.Undo()) { } Assert.True(wb.IsDirty); wb.MarkSaved(); Assert.False(wb.IsDirty); }
    [Fact] public void Undo_RecalcsAndRaisesEvent() { var wb = new Workbook(); wb.SetCell("B1","=A1*2"); wb.SetCell("A1","3"); int n = 0; wb.CellsChanged += (_, _) => n++; wb.Undo(); Assert.Equal("N:0", wb.GetValue("B1").ToCanonicalString()); Assert.Equal(1, n); }

    // ---- Additional coverage ----
    [Fact]
    public void SameInput_NotDirty_NoEvent()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1");
        wb.MarkSaved();
        int n = 0;
        wb.CellsChanged += (_, _) => n++;
        wb.SetCell("a1", "1");
        Assert.False(wb.IsDirty);
        Assert.Equal(0, n);
    }

    [Fact]
    public void Batch_DuplicateBackToOriginal_IsNoOp()
    {
        // A1: "x" → "y" → "x" within one batch: the last edit wins and equals the pre-commit input.
        var wb = new Workbook();
        wb.SetCell("A1", "x");
        wb.MarkSaved();
        int n = 0;
        wb.CellsChanged += (_, _) => n++;
        wb.ApplyBatch([KV("A1", "y"), KV("a1", "x")]);
        Assert.Equal(0, n);
        Assert.False(wb.IsDirty);
        Assert.True(wb.Undo());
        Assert.False(wb.CanUndo);
        Assert.Empty(wb.GetUsedAddresses());
    }

    [Fact]
    public void Batch_AllNoOps_NoStep_NoEvent_ZeroRecalc()
    {
        var wb = new Workbook();
        wb.SetCell("B1", "=A1+1");
        wb.SetCell("A1", "1");
        wb.MarkSaved();
        int n = 0;
        wb.CellsChanged += (_, _) => n++;
        wb.ApplyBatch([KV("A1", "1"), KV("B1", "=A1+1")]);
        Assert.Equal(0, n);
        Assert.False(wb.IsDirty);
        Assert.Equal(0, wb.LastRecalc.EvaluatedFormulaCount);
    }

    [Fact]
    public void Batch_OneEvent_AllChangedAddresses_RecalcCountOverUnion()
    {
        var wb = new Workbook();
        wb.SetCell("C1", "=A1+B1");
        wb.SetCell("D1", "=C1*2");
        wb.SetCell("E1", "=7");
        var got = new List<IReadOnlyCollection<string>>();
        wb.CellsChanged += (_, e) => got.Add(e.Addresses);
        wb.ApplyBatch([KV("A1", "1"), KV("B1", "2"), KV("F1", "=5")]);
        Assert.Single(got);
        Assert.Equal(new[] { "A1", "B1", "C1", "D1", "F1" }, got[0]);
        Assert.Equal("N:6", wb.GetValue("D1").ToCanonicalString());
        // {A1, B1, F1} ∪ dependents = {C1, D1, F1}: three formula cells; E1 is untouched.
        Assert.Equal(3, wb.LastRecalc.EvaluatedFormulaCount);
    }

    [Fact]
    public void Batch_OverlongInput_ThrowsBeforeApplying()
    {
        var wb = new Workbook();
        Assert.Throws<ArgumentException>(() => wb.ApplyBatch([KV("A1", "1"), KV("B1", new string('x', Workbook.MaxInputLength + 1))]));
        Assert.Empty(wb.GetUsedAddresses());
        Assert.False(wb.CanUndo);
        Assert.False(wb.IsDirty);
    }

    [Fact]
    public void Batch_NullInput_Throws_NothingApplied()
    {
        var wb = new Workbook();
        Assert.ThrowsAny<ArgumentException>(() => wb.ApplyBatch([KV("A1", "1"), KV("B1", null!)]));
        Assert.Empty(wb.GetUsedAddresses());
    }

    [Fact]
    public void Batch_UndoRedo_RecalcCountAndEvent()
    {
        var wb = new Workbook();
        wb.SetCell("C1", "=A1+B1");
        wb.ApplyBatch([KV("A1", "1"), KV("B1", "2")]);
        int n = 0;
        wb.CellsChanged += (_, _) => n++;
        Assert.True(wb.Undo());
        Assert.Equal(1, n);
        Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal("N:0", wb.GetValue("C1").ToCanonicalString());
        Assert.True(wb.Redo());
        Assert.Equal(2, n);
        Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal("N:3", wb.GetValue("C1").ToCanonicalString());
    }

    [Fact]
    public void UndoRedo_Cycle_RestoresCircAndValues()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "=B1");
        wb.SetCell("B1", "=A1");
        Assert.Equal("E:#CIRC!", wb.GetValue("A1").ToCanonicalString());
        wb.Undo();
        Assert.Equal("N:0", wb.GetValue("A1").ToCanonicalString());
        Assert.Equal("", wb.GetInput("B1"));
        wb.Redo();
        Assert.Equal("E:#CIRC!", wb.GetValue("A1").ToCanonicalString());
        Assert.Equal("E:#CIRC!", wb.GetValue("B1").ToCanonicalString());
    }

    [Fact]
    public void Redo_RedoStackSurvivesUntilNewMutation()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1");
        wb.SetCell("A1", "2");
        wb.Undo();
        wb.Undo();
        Assert.True(wb.CanRedo);
        Assert.False(wb.CanUndo);
        Assert.True(wb.Redo());
        Assert.Equal("1", wb.GetInput("A1"));
        Assert.True(wb.Redo());
        Assert.Equal("2", wb.GetInput("A1"));
        Assert.False(wb.Redo());
    }

    [Fact]
    public void Dirty_RedoToSavedIsClean()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1");
        wb.SetCell("A1", "2");
        wb.MarkSaved();
        wb.Undo();
        Assert.True(wb.IsDirty);
        wb.Redo();
        Assert.False(wb.IsDirty);
    }

    [Fact]
    public void Dirty_SavedStepIsOldestDropped_UndoAllIsClean()
    {
        // Saved after step 1; steps 2..101 push step 1 out, so step 1 becomes the base: undo-all returns to it.
        var wb = new Workbook();
        wb.SetCell("A1", "0");
        wb.MarkSaved();
        for (int i = 1; i <= 100; i++) wb.SetCell("A1", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        while (wb.Undo()) { }
        Assert.Equal("0", wb.GetInput("A1"));
        Assert.False(wb.IsDirty);
    }

    [Fact]
    public void FromSnapshot_NotDirty_NoUndo()
    {
        var wb = Workbook.FromSnapshot([KV("A1", "1")], [KV("Rate", "$A$1")]);
        Assert.False(wb.IsDirty);
        Assert.False(wb.CanUndo);
        Assert.False(wb.CanRedo);
        Assert.False(wb.Undo());
        Assert.Equal("1", wb.GetInput("A1"));
    }

    // ---- Names in steps (internal path that Phase 3 DefineName/DeleteName will use) ----
    [Fact]
    public void Names_UndoRedo_RestoresNamesExactlyAndRecalcs()
    {
        var wb = Workbook.FromSnapshot([KV("A1", "2"), KV("B1", "3"), KV("C1", "=Rate*10")], [KV("rate", "$A$1")]);
        Assert.Equal("N:20", wb.GetValue("C1").ToCanonicalString());
        int n = 0;
        wb.CellsChanged += (_, _) => n++;
        wb.ReplaceNames([KV("Rate", "$B$1"), KV("Other", "$a$1:$b$2")]);
        Assert.Equal(new[] { KV("Other", "$A$1:$B$2"), KV("Rate", "$B$1") }, wb.GetNames());
        Assert.Equal("N:30", wb.GetValue("C1").ToCanonicalString());
        Assert.Equal(1, n);
        Assert.True(wb.IsDirty);

        Assert.True(wb.Undo());
        Assert.Equal(new[] { KV("rate", "$A$1") }, wb.GetNames());
        Assert.Equal("N:20", wb.GetValue("C1").ToCanonicalString());
        Assert.False(wb.IsDirty);

        Assert.True(wb.Redo());
        Assert.Equal(new[] { KV("Other", "$A$1:$B$2"), KV("Rate", "$B$1") }, wb.GetNames());
        Assert.Equal("N:30", wb.GetValue("C1").ToCanonicalString());
    }

    [Fact]
    public void Names_DeletedThenUndone_NameErrorThenBack()
    {
        var wb = Workbook.FromSnapshot([KV("A1", "4"), KV("C1", "=Rate")], [KV("Rate", "$A$1")]);
        wb.ReplaceNames([]);
        Assert.Equal("E:#NAME?", wb.GetValue("C1").ToCanonicalString());
        // With the name gone, A1 is no longer a precedent of C1.
        wb.SetCell("A1", "5");
        Assert.Equal(0, wb.LastRecalc.EvaluatedFormulaCount);
        wb.Undo();
        wb.Undo();
        Assert.Equal("N:4", wb.GetValue("C1").ToCanonicalString());
        wb.SetCell("A1", "6");
        Assert.Equal("N:6", wb.GetValue("C1").ToCanonicalString());
    }

    [Fact]
    public void Names_Identical_NoStep()
    {
        var wb = Workbook.FromSnapshot([], [KV("Rate", "$A$1")]);
        wb.ReplaceNames([KV("Rate", "$a$1")]);
        Assert.False(wb.CanUndo);
        Assert.False(wb.IsDirty);
    }

    [Fact]
    public void Names_CasingChangeIsAStep()
    {
        var wb = Workbook.FromSnapshot([], [KV("Rate", "$A$1")]);
        wb.ReplaceNames([KV("RATE", "$A$1")]);
        Assert.True(wb.CanUndo);
        wb.Undo();
        Assert.Equal(new[] { KV("Rate", "$A$1") }, wb.GetNames());
    }

    [Fact]
    public void Names_Invalid_Throws_Unchanged()
    {
        var wb = Workbook.FromSnapshot([], [KV("Rate", "$A$1")]);
        Assert.Throws<ArgumentException>(() => wb.ReplaceNames([KV("Ok", "$B$1"), KV("A1", "$A$1")]));
        Assert.Throws<ArgumentException>(() => wb.ReplaceNames([KV("Ok", "$B$1"), KV("ok", "$A$1")]));
        Assert.Equal(new[] { KV("Rate", "$A$1") }, wb.GetNames());
        Assert.False(wb.CanUndo);
    }

    // ---- UndoHistory unit behaviour ----
    private static EditStep Step(long id) => new(id, [new CellEdit(1025, "", "x")], null, null);

    [Fact]
    public void History_PositionsAndBase()
    {
        var h = new UndoHistory(2);
        Assert.Equal(0, h.BaseId);
        Assert.Equal(0, h.CurrentPosition);
        h.Push(Step(1));
        h.Push(Step(2));
        h.Push(Step(3));
        Assert.Equal(1, h.BaseId);
        Assert.Equal(3, h.CurrentPosition);
        Assert.True(h.TryUndo(out var s));
        Assert.Equal(3, s.Id);
        Assert.Equal(2, h.CurrentPosition);
        Assert.True(h.TryUndo(out _));
        Assert.Equal(1, h.CurrentPosition);
        Assert.False(h.TryUndo(out _));
        Assert.True(h.CanRedo);
        Assert.True(h.TryRedo(out s));
        Assert.Equal(2, s.Id);
    }

    [Fact]
    public void History_SavedOnRedoSide_DroppedLater_StaysLost()
    {
        // Saved at 1; undo to 0; push 2 (discards redo 1) → lost even though position 1 never returns.
        var h = new UndoHistory(5);
        h.Push(Step(1));
        h.MarkSaved();
        h.TryUndo(out _);
        Assert.True(h.IsDirty);
        h.Push(Step(2));
        Assert.True(h.IsDirty);
        h.TryUndo(out _);
        Assert.True(h.IsDirty);
        h.MarkSaved();
        Assert.False(h.IsDirty);
    }

    [Fact]
    public void History_SavedReachableViaRedo_CleanAgain()
    {
        // limit 2: steps 1,2 pushed; saved at 2; undo both (redo holds 2,1). Saved stays reachable via redo.
        var h = new UndoHistory(2);
        h.Push(Step(1));
        h.Push(Step(2));
        h.MarkSaved();
        h.TryUndo(out _);
        h.TryUndo(out _);
        Assert.True(h.IsDirty);
        h.TryRedo(out _);
        h.TryRedo(out _);
        Assert.False(h.IsDirty);
    }
}
