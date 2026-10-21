using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Gridline.Core.Tests;

/// <summary>Random mutation sequences are fully reversible and replayable (§9.3).</summary>
public class UndoProperties
{
    private static readonly string[] Pool = ["A1", "B1", "C1", "A2", "B2", "C2", "A3", "B3", "C3"];

    // Sheets the generator may add, rename or delete (CR1.1, CR1.3); "My Data" needs quoting. Sheet1 is never
    // renamed or deleted. An op naming a sheet that does not exist (or a duplicate name on add/rename) must throw
    // ArgumentException and change nothing; every other op must succeed (see Expect).
    private static readonly string[] SheetPool = ["Data", "My Data", "S3"];

    // Rename targets include case-only variants of pool names.
    private static readonly string[] RenamePool = ["Data", "My Data", "S3", "DATA", "my data"];

    // Formulas reference the pool, so ops cause recalcs, ranges, and cycles (e.g. =SUM(A1:C3) inside the pool).
    private static readonly string[] Inputs =
        ["", "", "1", "2", "-3.5", "x", "TRUE", "=A1+1", "=B2*2", "=SUM(A1:C3)", "=C3", "=A1", "=B1&\"!\"", "=IF(A2>1,B3,C1)", "= 1 +  2", "=1/0", "=Rate+1", "=SUM(Tot)", "=tot",
         "=Data!A1+1", "=SUM('My Data'!A1:C3)", "=Sheet1!B2", "=S3!C3*2"];

    /// <summary>
    /// One op: "Set" (a SetCell, one edit), "Batch" (an ApplyBatch of 0..6 edits; addresses may repeat, any case)
    /// "Paste" (CopyPaste; Edits[0] = (source range, destination)) or "Save" (MarkSaved, no edits).
    /// </summary>
    public sealed record Op(string Kind, (string Address, string Input)[] Edits, string? Sheet = null)
    {
        public override string ToString() => Kind + (Sheet is null ? "" : "@" + Sheet) + "[" + string.Join(", ", Edits.Select(e => e.Address + "=" + e.Input)) + "]";
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
        (3, Gen.Elements(SheetPool.Append("sheet1").ToArray()).SelectMany(sh => EditGen.Select(e => new Op("Set", [(Gridline.Core.Addressing.SheetNames.Format(sh) + "!" + e.Item1, e.Item2)], sh)))),
        (1, Gen.Elements(SheetPool).SelectMany(o => Gen.Elements(RenamePool).Select(n => new Op("RenameSheet", [(o, n)])))),
        (1, Gen.Elements(SheetPool).Select(n => new Op("DeleteSheet", [(n, "")]))),
        (2, Gen.Elements(SheetPool.Append("Sheet1").ToArray()).SelectMany(sh => Gen.Elements("SInsRows", "SDelRows").SelectMany(k =>
            Gen.Choose(1, 4).SelectMany(at => Gen.Choose(1, 2).Select(n =>
                new Op(k, [(at.ToString(System.Globalization.CultureInfo.InvariantCulture), n.ToString(System.Globalization.CultureInfo.InvariantCulture))], sh)))))),
        (1, Gen.Constant(new Op("Save", []))));

    private static readonly Gen<Op[]> OpsGen = Gen.Choose(0, 30).SelectMany(n => Gen.ArrayOf(OpGen, n));

    private static int Int(string s) => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static bool HasSheet(Workbook wb, string name) => wb.SheetNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="action"/>: it must throw exactly <typeparamref name="TEx"/> when <paramref name="fails"/>
    /// (the expected failures: a missing sheet, a duplicate name, an out-of-range index), and must succeed otherwise.
    /// </summary>
    private static void Expect<TEx>(bool fails, Action action) where TEx : Exception
    {
        if (fails) Assert.Throws<TEx>(action);
        else action();
    }

    private static void Run(Workbook wb, Op op)
    {
        string a0 = op.Edits.Length > 0 ? op.Edits[0].Address : "", i0 = op.Edits.Length > 0 ? op.Edits[0].Input : "";
        switch (op.Kind)
        {
            case "Batch": wb.ApplyBatch(op.Edits.Select(e => KeyValuePair.Create(e.Address, e.Input)).ToArray()); break;
            case "Paste": wb.CopyPaste(a0, i0); break;
            case "Set":
                // Unqualified always succeeds; qualified fails only when its sheet does not exist.
                Expect<ArgumentException>(op.Sheet is not null && !HasSheet(wb, op.Sheet), () => wb.SetCell(a0, i0));
                break;
            case "Define": wb.DefineName(a0, i0); break;
            case "InsRows": wb.InsertRows(Int(a0), Int(i0)); break;
            case "DelRows": wb.DeleteRows(Int(a0), Int(i0)); break;
            case "InsCols": wb.InsertColumns(Int(a0), Int(i0)); break;
            case "DelCols": wb.DeleteColumns(Int(a0), Int(i0)); break;
            case "SInsRows":
                Expect<ArgumentException>(!HasSheet(wb, op.Sheet!), () => wb.InsertRows(op.Sheet!, Int(a0), Int(i0)));
                break;
            case "SDelRows":
                Expect<ArgumentException>(!HasSheet(wb, op.Sheet!), () => wb.DeleteRows(op.Sheet!, Int(a0), Int(i0)));
                break;
            case "Delete":
                Expect<ArgumentException>(!wb.GetNames().Any(n => n.Key.Equals(a0, StringComparison.OrdinalIgnoreCase)), () => wb.DeleteName(a0));
                break;
            case "AddSheet":
                // -1 means append; a duplicate name or an index past the end changes nothing.
                int at = Int(i0);
                if (HasSheet(wb, a0)) Expect<ArgumentException>(true, () => wb.AddSheet(a0, at < 0 ? null : at));
                else Expect<ArgumentOutOfRangeException>(at > wb.SheetNames.Count, () => wb.AddSheet(a0, at < 0 ? null : at));
                break;
            case "MoveSheet":
                if (!HasSheet(wb, a0)) Expect<ArgumentException>(true, () => wb.MoveSheet(a0, Int(i0)));
                else Expect<ArgumentOutOfRangeException>(Int(i0) >= wb.SheetNames.Count, () => wb.MoveSheet(a0, Int(i0)));
                break;
            case "Activate":
                Expect<ArgumentException>(!HasSheet(wb, a0), () => wb.ActiveSheet = a0);
                break;
            case "RenameSheet":
                // Fails for a missing old sheet, or a new name held by a different sheet (a case-only rename is fine).
                bool dup = HasSheet(wb, i0) && !i0.Equals(a0, StringComparison.OrdinalIgnoreCase);
                Expect<ArgumentException>(!HasSheet(wb, a0) || dup, () => wb.RenameSheet(a0, i0));
                break;
            case "DeleteSheet":
                Expect<ArgumentException>(!HasSheet(wb, a0), () => wb.DeleteSheet(a0));
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
    private static string[] State(Workbook wb) => Inputs_(wb).Concat(Values(wb)).ToArray();
    private static KeyValuePair<string, string>[] UsedCells(Workbook wb) => wb.GetUsedAddresses().Select(a => KeyValuePair.Create(a, wb.GetInput(a))).ToArray();
    private static string[] Values(Workbook wb) => Pool.Select(a => "Sheet1!" + a).Concat(wb.GetUsedAddresses()).Select(a => a + "=" + wb.GetValue(a).ToCanonicalString()).ToArray();

    /// <summary>
    /// A workbook with the given layout, names and cells built by the multi-sheet <see cref="Workbook.FromSnapshot"/>,
    /// i.e. a real full recalc (CR1.5): the oracle for "incremental equals full recalc".
    /// </summary>
    private static Workbook Fresh(string[] sheets, KeyValuePair<string, string>[] cells, IReadOnlyList<KeyValuePair<string, string>> names)
    {
        var bySheet = sheets.ToDictionary(n => n, _ => new List<KeyValuePair<string, string>>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (address, input) in cells)
        {
            Assert.True(Gridline.Core.Addressing.QualifiedAddress.TryParseCell(address, out string? sheet, out var cell));
            bySheet[sheet ?? sheets[0]].Add(KeyValuePair.Create(cell.ToString(), input));
        }
        var snaps = sheets.Select(n => new SheetSnapshot(n, bySheet[n])).ToArray();
        return Workbook.FromSnapshot(snaps, names, sheets[0]);
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
            // State (inputs, names, layout, values) after each recorded step; snaps[k] = after k steps. Every single
            // undo and redo must land exactly on the neighbouring snapshot (CR1.3: rename/delete restore verbatim).
            var snaps = new List<string[]> { State(wb) };
            foreach (var op in ops)
            {
                var before = Inputs_(wb);
                Run(wb, op);
                if (op.Kind == "Save") saved = steps;
                else if (!before.SequenceEqual(Inputs_(wb))) { steps++; snaps.Add(State(wb)); }
                dirtyOk &= wb.IsDirty == (steps != saved) && wb.CanUndo == (steps > 0);
            }
            var postInputs = Inputs_(wb);
            var postValues = Values(wb);
            var postNames = wb.GetNames();
            var postCells = UsedCells(wb);
            var postSheets = wb.SheetNames.ToArray();

            int undone = 0;
            bool stepwise = true;
            while (wb.Undo())
            {
                undone++;
                dirtyOk &= wb.IsDirty == (steps - undone != saved);
                stepwise &= undone <= steps && State(wb).SequenceEqual(snaps[steps - undone]);
            }
            bool initial = Inputs_(wb).SequenceEqual(Inputs_(new Workbook())) && wb.GetNames().Count == 0 && wb.GetUsedAddresses().Count == 0
                           && wb.SheetNames.SequenceEqual(["Sheet1"]) && wb.ActiveSheet == "Sheet1"
                           && Pool.All(a => wb.GetValue(a).Kind == ValueKind.Blank)
                           && wb.IsDirty == (saved != 0) && !wb.CanUndo && undone == steps;

            int redone = 0;
            while (wb.Redo())
            {
                redone++;
                dirtyOk &= wb.IsDirty == (redone != saved);
                stepwise &= redone <= steps && State(wb).SequenceEqual(snaps[redone]);
            }
            // Incremental recalc after redo agrees with a full recalc of the same inputs.
            var fresh = Fresh(postSheets, postCells, postNames);
            bool replayed = Inputs_(wb).SequenceEqual(postInputs) && Values(wb).SequenceEqual(postValues)
                            && Values(fresh).SequenceEqual(postValues) && redone == undone && !wb.CanRedo;

            return (initial && replayed && dirtyOk && stepwise).Label($"initial={initial} replayed={replayed} dirtyOk={dirtyOk} stepwise={stepwise} steps={steps} saved={saved} undone={undone} redone={redone}");
        });
}
