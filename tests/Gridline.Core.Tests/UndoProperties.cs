using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Gridline.Core.Tests;

/// <summary>Random mutation sequences are fully reversible and replayable (§9.3).</summary>
public class UndoProperties
{
    private static readonly string[] Pool = ["A1", "B1", "C1", "A2", "B2", "C2", "A3", "B3", "C3"];

    // Sheets the generator may add (CR1.1); "My Data" needs quoting. Ops on a sheet that does not exist yet are
    // skipped at run time (ArgumentException, no change).
    private static readonly string[] SheetPool = ["Data", "My Data", "S3"];

    // Formulas reference the pool, so ops cause recalcs, ranges, and cycles (e.g. =SUM(A1:C3) inside the pool).
    private static readonly string[] Inputs =
        ["", "", "1", "2", "-3.5", "x", "TRUE", "=A1+1", "=B2*2", "=SUM(A1:C3)", "=C3", "=A1", "=B1&\"!\"", "=IF(A2>1,B3,C1)", "= 1 +  2", "=1/0", "=Rate+1", "=SUM(Tot)", "=tot",
         "=Data!A1+1", "=SUM('My Data'!A1:C3)", "=Sheet1!B2", "=S3!C3*2"];

    /// <summary>
    /// One op: "Set" (a SetCell, one edit), "Batch" (an ApplyBatch of 0..6 edits; addresses may repeat, any case)
    /// "Paste" (CopyPaste; Edits[0] = (source range, destination)) or "Save" (MarkSaved, no edits).
    /// </summary>
    public sealed record Op(string Kind, (string Address, string Input)[] Edits)
    {
        public override string ToString() => Kind + "[" + string.Join(", ", Edits.Select(e => e.Address + "=" + e.Input)) + "]";
    }

    private static readonly Gen<string> Address =
        Gen.Elements(Pool).SelectMany(a => Gen.Elements(a, a.ToLowerInvariant()));

    private static readonly Gen<(string, string)> EditGen =
        Address.SelectMany(a => Gen.Elements(Inputs).Select(i => (a, i)));

    private static readonly string[] Ranges = ["A1", "B2", "A1:A3", "A1:C1", "B1:C2", "A1:C3", "c3", "b2:a1"];

    // Destinations are chosen so the pasted rectangle stays inside the pool (A1:C3), where the model observes it.
    private static readonly Gen<Op> PasteGen =
        Gen.Elements(Ranges).SelectMany(r =>
        {
            var rg = Gridline.Core.Addressing.RangeAddress.Parse(r);
            var dests = Pool.Where(p =>
            {
                var a = Gridline.Core.Addressing.CellAddress.Parse(p);
                return a.Row + rg.Rows - 1 <= 3 && a.Col + rg.Cols - 1 <= 3;
            }).ToArray();
            return Gen.Elements(dests).SelectMany(d => Gen.Elements(d, d.ToLowerInvariant())).Select(d => new Op("Paste", [(r, d)]));
        });

    // Insert/delete rows or columns (§9.5): Edits[0] = (at, count). Cells pushed out of the pool are still observed
    // through the used-cell snapshot, and later ops may pull them back.
    private static readonly Gen<Op> StructuralGen =
        Gen.Elements("InsRows", "DelRows", "InsCols", "DelCols").SelectMany(k =>
            Gen.Choose(1, 4).SelectMany(at => Gen.Choose(1, 2).Select(n =>
                new Op(k, [(at.ToString(System.Globalization.CultureInfo.InvariantCulture), n.ToString(System.Globalization.CultureInfo.InvariantCulture))]))));

    private static readonly Gen<Op> OpGen = Gen.Frequency(
        (4, EditGen.Select(e => new Op("Set", [e]))),
        (4, Gen.Choose(0, 6).SelectMany(n => Gen.ArrayOf(EditGen, n)).Select(es => new Op("Batch", es))),
        (3, PasteGen),
        (2, Gen.Elements("Rate", "rate", "Tot").SelectMany(n => Gen.Elements("$A$1", "$B$2", "$A$1:$B$2", "$A$1:$A$1").Select(r => new Op("Define", [(n, r)])))),
        (1, Gen.Elements("Rate", "rate", "Tot", "Nope").Select(n => new Op("Delete", [(n, "")]))),
        (2, StructuralGen),
        (2, Gen.Elements(SheetPool).SelectMany(n => Gen.Choose(-1, 3).Select(i => new Op("AddSheet", [(n, i.ToString(System.Globalization.CultureInfo.InvariantCulture))])))),
        (1, Gen.Elements(SheetPool.Append("Sheet1").ToArray()).SelectMany(n => Gen.Choose(0, 3).Select(i => new Op("MoveSheet", [(n, i.ToString(System.Globalization.CultureInfo.InvariantCulture))])))),
        (1, Gen.Elements(SheetPool.Append("Sheet1").ToArray()).Select(n => new Op("Activate", [(n, "")]))),
        (3, Gen.Elements(SheetPool.Append("sheet1").ToArray()).SelectMany(sh => EditGen.Select(e => new Op("Set", [(Gridline.Core.Addressing.SheetNames.Format(sh) + "!" + e.Item1, e.Item2)])))),
        (1, Gen.Constant(new Op("Save", []))));

    private static readonly Gen<Op[]> OpsGen = Gen.Choose(0, 30).SelectMany(n => Gen.ArrayOf(OpGen, n));

    private static void Run(Workbook wb, Op op)
    {
        switch (op.Kind)
        {
            case "Batch": wb.ApplyBatch(op.Edits.Select(e => KeyValuePair.Create(e.Address, e.Input)).ToArray()); break;
            case "Paste": wb.CopyPaste(op.Edits[0].Address, op.Edits[0].Input); break;
            case "Set":
                try { wb.SetCell(op.Edits[0].Address, op.Edits[0].Input); } catch (ArgumentException) { /* sheet not added yet */ }
                break;
            case "Define": wb.DefineName(op.Edits[0].Address, op.Edits[0].Input); break;
            case "InsRows": wb.InsertRows(int.Parse(op.Edits[0].Address, System.Globalization.CultureInfo.InvariantCulture), int.Parse(op.Edits[0].Input, System.Globalization.CultureInfo.InvariantCulture)); break;
            case "DelRows": wb.DeleteRows(int.Parse(op.Edits[0].Address, System.Globalization.CultureInfo.InvariantCulture), int.Parse(op.Edits[0].Input, System.Globalization.CultureInfo.InvariantCulture)); break;
            case "InsCols": wb.InsertColumns(int.Parse(op.Edits[0].Address, System.Globalization.CultureInfo.InvariantCulture), int.Parse(op.Edits[0].Input, System.Globalization.CultureInfo.InvariantCulture)); break;
            case "DelCols": wb.DeleteColumns(int.Parse(op.Edits[0].Address, System.Globalization.CultureInfo.InvariantCulture), int.Parse(op.Edits[0].Input, System.Globalization.CultureInfo.InvariantCulture)); break;
            case "Delete":
                try { wb.DeleteName(op.Edits[0].Address); } catch (ArgumentException) { /* unknown name: no change */ }
                break;
            case "AddSheet":
                // -1 means append; a duplicate name or out-of-range index changes nothing.
                int at = int.Parse(op.Edits[0].Input, System.Globalization.CultureInfo.InvariantCulture);
                try { wb.AddSheet(op.Edits[0].Address, at < 0 ? null : at); } catch (ArgumentException) { }
                break;
            case "MoveSheet":
                try { wb.MoveSheet(op.Edits[0].Address, int.Parse(op.Edits[0].Input, System.Globalization.CultureInfo.InvariantCulture)); } catch (ArgumentException) { }
                break;
            case "Activate":
                try { wb.ActiveSheet = op.Edits[0].Address; } catch (ArgumentException) { }
                break;
            default: wb.MarkSaved(); break;
        }
    }

    // Pool inputs, then the names, the sheet layout, then every used cell (structural ops can move cells outside the pool).
    // The pool is read on Sheet1, which never moves away from existence.
    private static string[] Inputs_(Workbook wb) => Pool.Select(a => wb.GetInput("Sheet1!" + a))
        .Append(string.Join(";", wb.GetNames().Select(n => n.Key + "=" + n.Value)))
        .Append(string.Join("|", wb.SheetNames))
        .Append(string.Join("\u0001", wb.GetUsedAddresses().Select(a => a + "=" + wb.GetInput(a)))).ToArray();
    private static KeyValuePair<string, string>[] UsedCells(Workbook wb) => wb.GetUsedAddresses().Select(a => KeyValuePair.Create(a, wb.GetInput(a))).ToArray();
    private static string[] Values(Workbook wb) => Pool.Select(a => "Sheet1!" + a).Concat(wb.GetUsedAddresses()).Select(a => a + "=" + wb.GetValue(a).ToCanonicalString()).ToArray();

    /// <summary>
    /// A workbook with the given layout, names and cells, recalculated in one go: <see cref="Workbook.FromSnapshot"/>
    /// for one sheet; with several, sheets are added and ordered first, then names, then all cells in one batch.
    /// </summary>
    private static Workbook Fresh(string[] sheets, KeyValuePair<string, string>[] cells, IReadOnlyList<KeyValuePair<string, string>> names)
    {
        if (sheets.Length == 1) return Workbook.FromSnapshot(cells, names);
        var wb = new Workbook();
        foreach (var n in sheets) if (n != "Sheet1") wb.AddSheet(n);
        for (int i = 0; i < sheets.Length; i++) wb.MoveSheet(sheets[i], i);
        wb.ReplaceNames(names);
        wb.ApplyBatch(cells);
        return wb;
    }

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
            var postNames = wb.GetNames();
            var postCells = UsedCells(wb);
            var postSheets = wb.SheetNames.ToArray();

            int undone = 0;
            while (wb.Undo()) { undone++; dirtyOk &= wb.IsDirty == (steps - undone != saved); }
            bool initial = Inputs_(wb).SequenceEqual(Inputs_(new Workbook())) && wb.GetNames().Count == 0 && wb.GetUsedAddresses().Count == 0
                           && wb.SheetNames.SequenceEqual(["Sheet1"]) && wb.ActiveSheet == "Sheet1"
                           && Pool.All(a => wb.GetValue(a).Kind == ValueKind.Blank)
                           && wb.IsDirty == (saved != 0) && !wb.CanUndo && undone == steps;

            int redone = 0;
            while (wb.Redo()) { redone++; dirtyOk &= wb.IsDirty == (redone != saved); }
            // Incremental recalc after redo agrees with a full recalc of the same inputs.
            var fresh = Fresh(postSheets, postCells, postNames);
            bool replayed = Inputs_(wb).SequenceEqual(postInputs) && Values(wb).SequenceEqual(postValues)
                            && Values(fresh).SequenceEqual(postValues) && redone == undone && !wb.CanRedo;

            return (initial && replayed && dirtyOk).Label($"initial={initial} replayed={replayed} dirtyOk={dirtyOk} steps={steps} saved={saved} undone={undone} redone={redone}");
        });
}
