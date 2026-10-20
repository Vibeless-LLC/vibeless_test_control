using Gridline.Core.Addressing;

namespace Gridline.Core.Tests;

/// <summary>Direct tests of <see cref="CellStore"/>'s bulk-write (merge) path.</summary>
public class CellStoreTests
{
    private static Cell Num(double v) => new(v.ToString(System.Globalization.CultureInfo.InvariantCulture), CellKind.Number, null, CellValue.FromNumber(v), null);

    private static void AssertConsistent(CellStore s, Dictionary<int, double> model)
    {
        Assert.Equal(model.Count, s.Count);
        foreach (var (key, v) in model)
        {
            Assert.True(s.TryGet(key, out var c));
            Assert.Equal(v, c.Value.Number);
        }
        int total = 0;
        for (int col = 1; col <= CellAddress.MaxColumns; col++)
        {
            var column = s.GetColumn(col);
            if (column is null) continue;
            Assert.Equal(column.Rows.Count, column.Cells.Count);
            for (int i = 0; i < column.Rows.Count; i++)
            {
                if (i > 0) Assert.True(column.Rows[i - 1] < column.Rows[i], "rows must be strictly ascending");
                Assert.True(s.TryGet(CellAddress.KeyOf(column.Rows[i], col), out var c));
                Assert.Same(c, column.Cells[i]);
            }
            total += column.Rows.Count;
        }
        Assert.Equal(model.Count, total);
    }

    [Fact]
    public void SetMany_BulkPath_MixedSetsReplacementsRemovals()
    {
        var s = new CellStore();
        var model = new Dictionary<int, double>();
        // Column 1 gets many writes (> 16 -> merge); column 2 only a few (<= 16 -> per-write path) in the same call.
        var initial = new List<(int, Cell?)>();
        for (int r = 1; r <= 40; r += 2) { initial.Add((CellAddress.KeyOf(r, 1), Num(r))); model[CellAddress.KeyOf(r, 1)] = r; }
        for (int r = 1; r <= 5; r++) { initial.Add((CellAddress.KeyOf(r * 3, 2), Num(100 + r))); model[CellAddress.KeyOf(r * 3, 2)] = 100 + r; }
        s.SetMany(initial);
        AssertConsistent(s, model);

        var writes = new List<(int, Cell?)>();
        for (int r = 2; r <= 40; r += 2) { writes.Add((CellAddress.KeyOf(r, 1), Num(1000 + r))); model[CellAddress.KeyOf(r, 1)] = 1000 + r; } // inserts between
        for (int r = 1; r <= 40; r += 6) { writes.Add((CellAddress.KeyOf(r, 1), Num(-r))); model[CellAddress.KeyOf(r, 1)] = -r; }           // replacements
        for (int r = 3; r <= 40; r += 6) { writes.Add((CellAddress.KeyOf(r, 1), null)); model.Remove(CellAddress.KeyOf(r, 1)); }          // removals
        writes.Add((CellAddress.KeyOf(99, 1), null));                                                                                        // remove of an absent cell
        writes.Add((CellAddress.KeyOf(500, 1), Num(7))); model[CellAddress.KeyOf(500, 1)] = 7;                                               // append
        writes.Add((CellAddress.KeyOf(3, 2), null)); model.Remove(CellAddress.KeyOf(3, 2));                                                  // small column: removal
        writes.Add((CellAddress.KeyOf(4, 2), Num(55))); model[CellAddress.KeyOf(4, 2)] = 55;                                                 // small column: insert
        writes.Add((CellAddress.KeyOf(6, 2), Num(66))); model[CellAddress.KeyOf(6, 2)] = 66;                                                 // small column: replace
        Assert.True(writes.Count > 16);
        s.SetMany(writes);
        AssertConsistent(s, model);

        // Remove everything in column 1 in one bulk call.
        var clear = model.Keys.Where(k => CellAddress.FromKey(k).Col == 1).Select(k => (k, (Cell?)null)).ToList();
        foreach (var (k, _) in clear) model.Remove(k);
        s.SetMany(clear);
        AssertConsistent(s, model);
    }

    [Fact]
    public void SetMany_SmallBatch_UsesPerWritePath()
    {
        var s = new CellStore();
        var model = new Dictionary<int, double>();
        var w = new List<(int, Cell?)>();
        for (int r = 10; r >= 1; r--) { w.Add((CellAddress.KeyOf(r, 3), Num(r))); model[CellAddress.KeyOf(r, 3)] = r; }
        s.SetMany(w);
        AssertConsistent(s, model);
    }
}
