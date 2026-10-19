using System.Diagnostics;
using Gridline.Core.Addressing;
using Gridline.Core.Dependencies;

namespace Gridline.Core.Tests.Dependencies;

public class DependencyGraphTests
{
    private static int K(string a) => CellAddress.Parse(a).Key;
    private static RangeAddress R(string r) => RangeAddress.Parse(r);

    private static FormulaDeps Cells(params string[] cells) => new(cells.Select(K).ToArray(), [], []);
    private static FormulaDeps Ranges(params string[] ranges) => new([], ranges.Select(R).ToArray(), []);
    private static FormulaDeps Names(params string[] names) => new([], [], names);

    private static RecalcPlan Plan(DependencyGraph g, IEnumerable<string> changed, IEnumerable<string>? names = null, Func<int, bool>? isFormula = null)
        => new RecalcPlanner(g).Plan(changed.Select(K), names ?? [], isFormula ?? (_ => false));

    private static int[] Keys(params string[] cells) => cells.Select(K).ToArray();

    [Fact]
    public void Chain_TopologicalOrder()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("B1"), Cells("A1"));
        g.SetDependencies(K("C1"), Cells("B1"));
        var p = Plan(g, ["A1"]);
        Assert.Equal(Keys("B1", "C1"), p.Order);
        Assert.Empty(p.CycleMembers);
    }

    [Fact]
    public void RangeDependents_Found()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("D1"), Ranges("A1:A10"));
        Assert.Contains(K("D1"), Plan(g, ["A5"]).Order);
        Assert.Empty(Plan(g, ["B5"]).Order);
    }

    [Fact]
    public void SelfReference_IsCycle()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("A1"), Cells("A1"));
        var p = Plan(g, ["A1"], isFormula: k => k == K("A1"));
        Assert.Equal(new HashSet<int>(Keys("A1")), p.CycleMembers);
        Assert.Equal(Keys("A1"), p.Order);
    }

    [Fact]
    public void Cycle_DependentsNotMembers()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("A1"), Cells("B1"));
        g.SetDependencies(K("B1"), Cells("A1"));
        g.SetDependencies(K("C1"), Cells("A1"));
        var formulas = new HashSet<int>(Keys("A1", "B1", "C1"));
        var p = Plan(g, ["A1"], isFormula: formulas.Contains);
        Assert.Equal(new HashSet<int>(Keys("A1", "B1")), p.CycleMembers);
        Assert.Equal(3, p.Order.Count);
        int c = p.Order.ToList().IndexOf(K("C1"));
        Assert.True(c > p.Order.ToList().IndexOf(K("A1")));
        Assert.True(c > p.Order.ToList().IndexOf(K("B1")));
    }

    [Fact]
    public void NameDependents()
    {
        var g = new DependencyGraph();
        g.SetNameTarget("RATE", R("B1"));
        g.SetDependencies(K("C1"), Names("RATE"));
        Assert.Contains(K("C1"), Plan(g, ["B1"]).Order);
        Assert.Contains(K("C1"), Plan(g, [], ["RATE"]).Order);
        Assert.Equal(Keys("C1"), g.NameDependents("RATE"));
    }

    [Fact]
    public void Recalc_LongChain_NoStackOverflow()
    {
        var g = new DependencyGraph();
        for (int r = 2; r <= 100_000; r++)
            g.SetDependencies(new CellAddress(r, 1).Key, new FormulaDeps([new CellAddress(r - 1, 1).Key], [], []));
        var p = Plan(g, ["A1"]);
        Assert.Equal(99_999, p.Order.Count);
        Assert.Equal(K("A2"), p.Order[0]);
        Assert.Equal(K("A100000"), p.Order[^1]);
        Assert.Empty(p.CycleMembers);
    }

    [Fact, Trait("Category", "Perf")]
    public void Recalc_RangeChain_NoStackOverflow() => AssertRangeChain(10_000, TimeSpan.FromSeconds(5));

    [Fact]
    public void Recalc_RangeChain_Correct() => AssertRangeChain(2_000, null);

    private static void AssertRangeChain(int last, TimeSpan? budget)
    {
        var g = new DependencyGraph();
        for (int n = 2; n <= last; n++)
            g.SetDependencies(new CellAddress(n, 1).Key,
                new FormulaDeps([], [new RangeAddress(new CellAddress(1, 1), new CellAddress(n - 1, 1))], []));
        var sw = Stopwatch.StartNew();
        var p = Plan(g, ["A1"]);
        sw.Stop();
        Assert.Equal(last - 1, p.Order.Count);
        if (budget is { } b) Assert.True(sw.Elapsed < b, $"took {sw.Elapsed}");
        var pos = new Dictionary<int, int>();
        for (int i = 0; i < p.Order.Count; i++) pos[p.Order[i]] = i;
        for (int n = 3; n <= last; n++)
            Assert.True(pos[new CellAddress(n - 1, 1).Key] < pos[new CellAddress(n, 1).Key]);
    }

    // ---- additional coverage ----

    [Fact]
    public void Diamond_EachCellOnce_PrecedentsFirst()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("B1"), Cells("A1"));
        g.SetDependencies(K("C1"), Cells("A1"));
        g.SetDependencies(K("D1"), new FormulaDeps(Keys("B1", "C1"), [R("A1:A1")], []));
        var p = Plan(g, ["A1"]);
        Assert.Equal(3, p.Order.Count);
        Assert.Equal(3, p.Order.Distinct().Count());
        Assert.Equal(K("D1"), p.Order[^1]);
    }

    [Fact]
    public void ChangedFormulaCell_IncludedFirst()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("B1"), Cells("A1"));
        g.SetDependencies(K("C1"), Cells("B1"));
        var p = Plan(g, ["B1"], isFormula: k => k == K("B1"));
        Assert.Equal(Keys("B1", "C1"), p.Order);
    }

    [Fact]
    public void SetDependencies_ReplaceAndRemove()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("C1"), new FormulaDeps(Keys("A1"), [R("E1:E5")], ["X"]));
        g.SetDependencies(K("C1"), Cells("B1"));
        Assert.Empty(g.DirectDependents(K("A1")));
        Assert.Empty(g.DirectDependents(K("E3")));
        Assert.Empty(g.NameDependents("X"));
        Assert.Equal(Keys("C1"), g.DirectDependents(K("B1")).Distinct());
        g.SetDependencies(K("C1"), null);
        Assert.Empty(g.DirectDependents(K("B1")));
    }

    [Fact]
    public void IdenticalRanges_Deduped_AndRemovedIndependently()
    {
        var g = new DependencyGraph();
        for (int r = 1; r <= 1000; r++)
            g.SetDependencies(new CellAddress(r, 2).Key, Ranges("$A$1:$A$100000"));
        Assert.Equal(1, g.DistinctRangeCount);
        Assert.Equal(1000, g.DirectDependents(K("A50000")).Count());
        g.SetDependencies(K("B1"), null);
        Assert.Equal(999, g.DirectDependents(K("A50000")).Count());
        for (int r = 2; r <= 1000; r++) g.SetDependencies(new CellAddress(r, 2).Key, null);
        Assert.Equal(0, g.DistinctRangeCount);
        Assert.Empty(g.DirectDependents(K("A50000")));
    }

    [Theory]
    [InlineData("A250:A260", "A256", true)]
    [InlineData("A250:A260", "A257", true)]
    [InlineData("A250:A260", "A249", false)]
    [InlineData("A250:A260", "A261", false)]
    [InlineData("A1:C99999", "B99999", true)]
    [InlineData("A1:C99999", "D5", false)]
    [InlineData("A1:C99999", "C100000", false)]
    [InlineData("C3:A1", "B2", true)]
    [InlineData("A1:ZZ100000", "ZZ100000", true)]
    public void RangeIndex_Boundaries(string range, string cell, bool expected)
    {
        var g = new DependencyGraph();
        // Unnormalized constructor input must be handled too.
        var parsed = R(range);
        var raw = range == "C3:A1" ? new RangeAddress(CellAddress.Parse("C3"), CellAddress.Parse("A1")) : parsed;
        g.SetDependencies(K("ZY1"), new FormulaDeps([], [raw], []));
        Assert.Equal(expected, g.DirectDependents(K(cell)).Contains(K("ZY1")));
    }

    [Fact]
    public void UndefinedName_KeepsDependents_DefinedLater()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("C1"), Names("RATE"));
        Assert.Empty(g.DirectDependents(K("B1")));
        Assert.Equal(Keys("C1"), Plan(g, [], ["RATE"]).Order);
        g.SetNameTarget("RATE", R("B1:B3"));
        Assert.Contains(K("C1"), g.DirectDependents(K("B2")));
        g.SetNameTarget("RATE", R("D1"));
        Assert.Empty(g.DirectDependents(K("B2")));
        Assert.Contains(K("C1"), g.DirectDependents(K("D1")));
        g.SetNameTarget("RATE", null);
        Assert.Empty(g.DirectDependents(K("D1")));
        Assert.Equal(Keys("C1"), g.NameDependents("RATE"));
        Assert.Empty(g.NameDependents("NOPE"));
    }

    [Fact]
    public void CycleThroughRangeAndName()
    {
        var g = new DependencyGraph();
        g.SetNameTarget("N", R("B1"));
        g.SetDependencies(K("A1"), Ranges("B1:B2"));   // A1 -> uses B1 via range
        g.SetDependencies(K("B1"), Names("N"));        // B1 uses itself via name
        g.SetDependencies(K("B2"), Cells("A1"));       // B2 uses A1; A1 uses B2 via range
        var formulas = new HashSet<int>(Keys("A1", "B1", "B2"));
        var p = new RecalcPlanner(g).PlanAll(formulas);
        Assert.Equal(new HashSet<int>(Keys("A1", "B1", "B2")), p.CycleMembers);
        Assert.Equal(3, p.Order.Count);
    }

    [Fact]
    public void PlanAll_OrdersEverything()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("C1"), Cells("B1"));
        g.SetDependencies(K("B1"), Cells("A1"));
        g.SetDependencies(K("Z9"), Cells("Y9"));
        var p = new RecalcPlanner(g).PlanAll(Keys("C1", "Z9", "B1"));
        Assert.Equal(3, p.Order.Count);
        Assert.True(p.Order.ToList().IndexOf(K("B1")) < p.Order.ToList().IndexOf(K("C1")));
        Assert.Empty(p.CycleMembers);
    }

    [Fact]
    public void PlanAll_LongChain_NoStackOverflow()
    {
        var g = new DependencyGraph();
        var all = new List<int>();
        for (int r = 100_000; r >= 2; r--)
        {
            int k = new CellAddress(r, 1).Key;
            g.SetDependencies(k, new FormulaDeps([new CellAddress(r - 1, 1).Key], [], []));
            all.Add(k);
        }
        var p = new RecalcPlanner(g).PlanAll(all);
        Assert.Equal(99_999, p.Order.Count);
        Assert.Equal(K("A2"), p.Order[0]);
    }

    [Fact]
    public void LongCycle_AllMembers()
    {
        var g = new DependencyGraph();
        const int n = 50_000;
        for (int r = 1; r <= n; r++)
            g.SetDependencies(new CellAddress(r, 1).Key, new FormulaDeps([new CellAddress(r == 1 ? n : r - 1, 1).Key], [], []));
        var p = Plan(g, ["A1"], isFormula: _ => true);
        Assert.Equal(n, p.CycleMembers.Count);
        Assert.Equal(n, p.Order.Count);
    }

    private static DependencyGraph SharedFullColumnGraph()
    {
        var g = new DependencyGraph();
        for (int r = 1; r <= 1000; r++)
            g.SetDependencies(new CellAddress(r, 2).Key, Ranges("$A$1:$A$100000"));
        return g;
    }

    private static int[] ColumnAKeys() => Enumerable.Range(1, 100_000).Select(r => new CellAddress(r, 1).Key).ToArray();

    [Fact]
    public void BulkChange_SharedRanges_EachDependentOnce()
    {
        var g = SharedFullColumnGraph();
        var keys = ColumnAKeys();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var p = new RecalcPlanner(g).Plan(keys, [], _ => false);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // O(changed + affected), not O(changed x fan-in) (~10^8 roots, ~400 MB+).
        Assert.True(allocated < 32L * 1024 * 1024, $"allocated {allocated:N0} bytes");
        Assert.Equal(1000, p.Order.Count);
        Assert.Equal(1000, p.Order.Distinct().Count());
        Assert.Empty(p.CycleMembers);
    }

    [Fact, Trait("Category", "Perf")]
    public void BulkChange_SharedRanges_Fast()
    {
        var g = SharedFullColumnGraph();
        var keys = ColumnAKeys();
        var planner = new RecalcPlanner(g);
        planner.Plan(keys, [], _ => false); // warm-up
        var sw = Stopwatch.StartNew();
        var p = planner.Plan(keys, [], _ => false);
        sw.Stop();
        Assert.Equal(1000, p.Order.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
    }

    [Fact]
    public void DistinctDirectDependentsOfMany_DedupesAcrossCellsRangesAndNames()
    {
        var g = new DependencyGraph();
        g.SetNameTarget("N", R("A1:A3"));
        g.SetDependencies(K("C1"), new FormulaDeps(Keys("A1", "A2"), [R("A1:A5")], ["N"]));
        g.SetDependencies(K("C2"), Names("N"));
        g.SetDependencies(K("C3"), Ranges("A4:A9"));
        g.SetDependencies(K("C4"), Cells("Z1"));
        var got = g.DistinctDirectDependentsOfMany(Keys("A1", "A2", "A3", "A4", "A1")).ToList();
        Assert.Equal(got.Count, got.Distinct().Count());
        Assert.Equal(new HashSet<int>(Keys("C1", "C2", "C3")), got.ToHashSet());
    }

    [Fact]
    public void Plan_DuplicateChangedKeys_Deduped()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("B1"), Cells("A1"));
        var p = new RecalcPlanner(g).Plan(Keys("A1", "A1", "B1", "B1"), ["X", "X"], k => k == K("B1"));
        Assert.Equal(Keys("B1"), p.Order);
    }

    [Fact]
    public void NoDependents_Empty()
    {
        var g = new DependencyGraph();
        g.SetDependencies(K("B1"), Cells("A1"));
        var p = Plan(g, ["Q7"]);
        Assert.Empty(p.Order);
        Assert.Empty(p.CycleMembers);
    }
}
