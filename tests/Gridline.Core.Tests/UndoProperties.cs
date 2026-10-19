using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Gridline.Core.Tests;

/// <summary>Random mutation sequences are fully reversible and replayable (§9.3).</summary>
public class UndoProperties
{
    private static readonly string[] Pool = ["A1", "B1", "C1", "A2", "B2", "C2", "A3", "B3", "C3"];

    // Formulas reference the pool, so ops cause recalcs, ranges, and cycles (e.g. =SUM(A1:C3) inside the pool).
    private static readonly string[] Inputs =
        ["", "", "1", "2", "-3.5", "x", "TRUE", "=A1+1", "=B2*2", "=SUM(A1:C3)", "=C3", "=A1", "=B1&\"!\"", "=IF(A2>1,B3,C1)", "= 1 +  2", "=1/0"];

    /// <summary>
    /// One op: "Set" (a SetCell, one edit), "Batch" (an ApplyBatch of 0..6 edits; addresses may repeat, any case)
    /// or "Save" (MarkSaved, no edits).
    /// </summary>
    public sealed record Op(string Kind, (string Address, string Input)[] Edits)
    {
        public override string ToString() => Kind + "[" + string.Join(", ", Edits.Select(e => e.Address + "=" + e.Input)) + "]";
    }

    private static readonly Gen<string> Address =
        Gen.Elements(Pool).SelectMany(a => Gen.Elements(a, a.ToLowerInvariant()));

    private static readonly Gen<(string, string)> EditGen =
        Address.SelectMany(a => Gen.Elements(Inputs).Select(i => (a, i)));

    private static readonly Gen<Op> OpGen = Gen.Frequency(
        (4, EditGen.Select(e => new Op("Set", [e]))),
        (4, Gen.Choose(0, 6).SelectMany(n => Gen.ArrayOf(EditGen, n)).Select(es => new Op("Batch", es))),
        (1, Gen.Constant(new Op("Save", []))));

    private static readonly Gen<Op[]> OpsGen = Gen.Choose(0, 30).SelectMany(n => Gen.ArrayOf(OpGen, n));

    private static void Run(Workbook wb, Op op)
    {
        switch (op.Kind)
        {
            case "Batch": wb.ApplyBatch(op.Edits.Select(e => KeyValuePair.Create(e.Address, e.Input)).ToArray()); break;
            case "Set": wb.SetCell(op.Edits[0].Address, op.Edits[0].Input); break;
            default: wb.MarkSaved(); break;
        }
    }

    private static string[] Inputs_(Workbook wb) => Pool.Select(wb.GetInput).ToArray();
    private static string[] Values(Workbook wb) => Pool.Select(a => wb.GetValue(a).ToCanonicalString()).ToArray();

    [Property(MaxTest = 200)]
    public Property RandomOps_UndoAll_RestoresInitial() =>
        Prop.ForAll(OpsGen.ToArbitrary(), ops =>
        {
            // Dirty model: at most 30 steps (below UndoLimit) and no undo before the end, so the history position
            // is just the number of recorded steps; an op records a step exactly when it changes some input.
            var wb = new Workbook();
            int steps = 0, saved = 0;
            bool dirtyOk = !wb.IsDirty;
            foreach (var op in ops)
            {
                var before = Inputs_(wb);
                Run(wb, op);
                if (op.Kind == "Save") saved = steps;
                else if (!before.SequenceEqual(Inputs_(wb))) steps++;
                dirtyOk &= wb.IsDirty == (steps != saved) && wb.CanUndo == (steps > 0);
            }
            var postInputs = Inputs_(wb);
            var postValues = Values(wb);

            int undone = 0;
            while (wb.Undo()) { undone++; dirtyOk &= wb.IsDirty == (steps - undone != saved); }
            bool initial = Inputs_(wb).All(i => i.Length == 0) && wb.GetUsedAddresses().Count == 0
                           && Pool.All(a => wb.GetValue(a).Kind == ValueKind.Blank)
                           && wb.IsDirty == (saved != 0) && !wb.CanUndo && undone == steps;

            int redone = 0;
            while (wb.Redo()) { redone++; dirtyOk &= wb.IsDirty == (redone != saved); }
            // Incremental recalc after redo agrees with a full recalc of the same inputs.
            var fresh = Workbook.FromSnapshot(Pool.Zip(postInputs).Where(p => p.Second.Length > 0).Select(p => KeyValuePair.Create(p.First, p.Second)).ToArray(), []);
            bool replayed = Inputs_(wb).SequenceEqual(postInputs) && Values(wb).SequenceEqual(postValues)
                            && Values(fresh).SequenceEqual(postValues) && redone == undone && !wb.CanRedo;

            return (initial && replayed && dirtyOk).Label($"initial={initial} replayed={replayed} dirtyOk={dirtyOk} steps={steps} saved={saved} undone={undone} redone={redone}");
        });
}
