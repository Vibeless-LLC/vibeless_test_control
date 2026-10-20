using Gridline.Core.Addressing;
using Gridline.Core.Dependencies;
using Gridline.Core.Evaluation;
using Gridline.Core.Functions;

namespace Gridline.Core.Tests.Dependencies;

/// <summary>Sheet-qualified global keys in the dependency graph and planner (CR1.2, §6.1, §6.7).</summary>
public class SheetGraphTests
{
    private static int K(string a) => CellAddress.Parse(a).Key;
    private static long G(int sheet, string a) => GlobalKey.Of(sheet, K(a));

    private static FormulaDeps Deps(SheetCell[]? cells = null, SheetRange[]? ranges = null, string[]? names = null) =>
        new(cells ?? [], ranges ?? [], names ?? []);

    private static Func<string, int?> Resolver(Dictionary<string, int> sheets) =>
        n => sheets.TryGetValue(n, out int id) ? id : null;

    private static Dictionary<string, int> Sheets(params (string Name, int Id)[] s)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, id) in s) d[n] = id;
        return d;
    }

    [Fact]
    public void GlobalKey_PacksAndUnpacks_PreservingOrder()
    {
        long g = GlobalKey.Of(3, K("ZZ100000"));
        Assert.Equal(3, GlobalKey.Sheet(g));
        Assert.Equal(K("ZZ100000"), GlobalKey.Cell(g));
        Assert.Equal(CellAddress.Parse("ZZ100000"), GlobalKey.Address(g));
        Assert.True(GlobalKey.Of(0, K("ZZ100000")) < GlobalKey.Of(1, K("A1")));
        Assert.True(GlobalKey.Of(1, K("A1")) < GlobalKey.Of(1, K("B1")));
        Assert.True(GlobalKey.Of(1, K("B1")) < GlobalKey.Of(1, K("A2")));
        Assert.Equal(K("C7"), GlobalKey.Of(0, K("C7"))); // sheet 0 keys equal the cell key
    }

    [Fact]
    public void QualifiedRef_ResolvedAtRegistration_AndRebound()
    {
        var g = new DependencyGraph();
        var sheets = Sheets(("Sheet1", 0));
        var resolve = Resolver(sheets);
        long f = G(0, "B1");
        g.SetDependencies(f, Deps(cells: [new("Data", K("A1"))]), resolve);
        long dataA1 = G(7, "A1");
        Assert.Empty(g.DirectDependents(dataA1));          // Data missing: not registered
        Assert.Equal([f], g.FormulasReferencingSheet("DATA"));
        sheets["Data"] = 7;
        Assert.Equal([f], g.Rebind("DATA", resolve));
        Assert.Equal([f], g.DirectDependents(dataA1));
        sheets.Remove("Data");
        g.Rebind("DATA", resolve);
        Assert.Empty(g.DirectDependents(dataA1));
        Assert.Equal([f], g.FormulasReferencingSheet("DATA"));
    }

    [Fact]
    public void Resolver_ReceivesNameAsWritten_IndexIsCaseInsensitive()
    {
        var g = new DependencyGraph();
        var seen = new List<string>();
        long f = G(0, "B1");
        g.SetDependencies(f, Deps(cells: [new("dAtA", K("A1"))]), n => { seen.Add(n); return 2; });
        Assert.Equal(["dAtA"], seen);
        Assert.Equal([f], g.FormulasReferencingSheet("DATA"));
        Assert.Equal([f], g.FormulasReferencingSheet("data"));
        Assert.Equal([f], g.DirectDependents(G(2, "A1")));
    }

    [Fact]
    public void Unqualified_ResolvesToOwnSheet()
    {
        var g = new DependencyGraph();
        var resolve = Resolver(Sheets(("Sheet1", 0), ("Data", 1)));
        long f0 = G(0, "B1"), f1 = G(1, "B1");
        g.SetDependencies(f0, Deps(cells: [new(null, K("A1"))]), resolve);
        g.SetDependencies(f1, Deps(cells: [new(null, K("A1"))], ranges: [new(null, RangeAddress.Parse("C1:C5"))]), resolve);
        Assert.Equal([f0], g.DirectDependents(G(0, "A1")));
        Assert.Equal([f1], g.DirectDependents(G(1, "A1")));
        Assert.Equal([f1], g.DirectDependents(G(1, "C3")));
        Assert.Empty(g.DirectDependents(G(0, "C3")));
        Assert.Empty(g.FormulasReferencingSheet("SHEET1"));
        Assert.Empty(g.FormulasReferencingSheet("DATA"));
    }

    [Fact]
    public void RangeIndex_IsPerSheet()
    {
        var g = new DependencyGraph();
        var resolve = Resolver(Sheets(("Sheet1", 0), ("Data", 1)));
        long f = G(0, "B1");
        g.SetDependencies(f, Deps(ranges: [new("Data", RangeAddress.Parse("A1:A10"))]), resolve);
        Assert.Empty(g.DirectDependents(G(0, "A5")));
        Assert.Equal([f], g.DirectDependents(G(1, "A5")));
        Assert.Equal([f], g.DistinctDirectDependentsOfMany([G(0, "A5"), G(1, "A5"), G(1, "A6")]));
        Assert.Equal([f], g.FormulasReferencingSheet("DATA"));

        // Same range on two sheets: two distinct entries, removed independently.
        long f2 = G(1, "B1");
        g.SetDependencies(f2, Deps(ranges: [new(null, RangeAddress.Parse("A1:A10"))]), resolve);
        long f3 = G(0, "C1");
        g.SetDependencies(f3, Deps(ranges: [new(null, RangeAddress.Parse("A1:A10"))]), resolve);
        Assert.Equal(2, g.DistinctRangeCount);
        Assert.Equal([f3], g.DirectDependents(G(0, "A5")));
        Assert.Equal(new HashSet<long> { f, f2 }, g.DirectDependents(G(1, "A5")).ToHashSet());
        g.SetDependencies(f, null, resolve);
        Assert.Equal([f2], g.DirectDependents(G(1, "A5")));
        Assert.Empty(g.FormulasReferencingSheet("DATA"));
    }

    [Fact]
    public void TallRange_IsPerSheet()
    {
        var g = new DependencyGraph();
        var resolve = Resolver(Sheets(("Sheet1", 0), ("Data", 1)));
        long f = G(0, "B1");
        g.SetDependencies(f, Deps(ranges: [new("Data", RangeAddress.Parse("A1:A100000"))]), resolve);
        Assert.Equal([f], g.DirectDependents(G(1, "A99999")));
        Assert.Empty(g.DirectDependents(G(0, "A99999")));
    }

    [Fact]
    public void Unregister_UsesStoredResolution_NotResolverAtRemovalTime()
    {
        var g = new DependencyGraph();
        var sheets = Sheets(("Sheet1", 0), ("Data", 1));
        var resolve = Resolver(sheets);
        long f = G(0, "B1");
        g.SetDependencies(f, Deps(cells: [new("Data", K("A1"))], ranges: [new("Data", RangeAddress.Parse("C1:C3"))]), resolve);
        sheets["Data"] = 5; // the sheet's mapping changes without a rebind
        g.SetDependencies(f, null, resolve);
        Assert.Empty(g.DirectDependents(G(1, "A1")));
        Assert.Empty(g.DirectDependents(G(1, "C2")));
        Assert.Equal(0, g.DistinctRangeCount);
        Assert.Empty(g.FormulasReferencingSheet("DATA"));
    }

    [Fact]
    public void Rebind_UnknownName_ReturnsEmpty()
    {
        var g = new DependencyGraph();
        Assert.Empty(g.Rebind("NOPE", _ => null));
        Assert.Empty(g.FormulasReferencingSheet("NOPE"));
    }

    [Fact]
    public void Rebind_MovesRegistrationToNewId_AndKeepsUnqualified()
    {
        var g = new DependencyGraph();
        var sheets = Sheets(("Sheet1", 0), ("Data", 1));
        var resolve = Resolver(sheets);
        long f = G(0, "B1");
        g.SetDependencies(f, Deps(cells: [new("Data", K("A1")), new(null, K("A2"))], ranges: [new("data", RangeAddress.Parse("D1:D2"))]), resolve);
        sheets["Data"] = 4;
        Assert.Equal([f], g.Rebind("DATA", resolve));
        Assert.Empty(g.DirectDependents(G(1, "A1")));
        Assert.Empty(g.DirectDependents(G(1, "D1")));
        Assert.Equal([f], g.DirectDependents(G(4, "A1")));
        Assert.Equal([f], g.DirectDependents(G(4, "D2")));
        Assert.Equal([f], g.DirectDependents(G(0, "A2")));
        Assert.Equal(1, g.DistinctRangeCount);
    }

    [Fact]
    public void NameTarget_OnSheet()
    {
        var g = new DependencyGraph();
        var resolve = Resolver(Sheets(("Sheet1", 0), ("Data", 1)));
        long f = G(0, "C1");
        g.SetDependencies(f, Deps(names: ["RATE"]), resolve);
        g.SetNameTarget("RATE", 1, RangeAddress.Parse("B1:B3"));
        Assert.Equal([f], g.DirectDependents(G(1, "B2")));
        Assert.Empty(g.DirectDependents(G(0, "B2")));
        g.SetNameTarget("RATE", 0, RangeAddress.Parse("B1:B3"));
        Assert.Equal([f], g.DirectDependents(G(0, "B2")));
        Assert.Empty(g.DirectDependents(G(1, "B2")));
        g.SetNameTarget("RATE", null, null);
        Assert.Empty(g.DirectDependents(G(0, "B2")));
        Assert.Equal([f], g.NameDependents("RATE"));
    }

    [Fact]
    public void Planner_CrossSheetCycle_AllMembersFlagged()
    {
        var g = new DependencyGraph();
        var resolve = Resolver(Sheets(("Sheet1", 0), ("Data", 1)));
        long s1a1 = G(0, "A1"), dataA1 = G(1, "A1"), s1b1 = G(0, "B1");
        g.SetDependencies(s1a1, Deps(cells: [new("Data", K("A1"))]), resolve);
        g.SetDependencies(dataA1, Deps(cells: [new("Sheet1", K("A1"))]), resolve);
        g.SetDependencies(s1b1, Deps(cells: [new(null, K("A1"))]), resolve);
        var formulas = new HashSet<long> { s1a1, dataA1, s1b1 };
        var p = new RecalcPlanner(g).Plan([s1a1], [], [], formulas.Contains);
        Assert.Equal(new HashSet<long> { s1a1, dataA1 }, p.CycleMembers);
        Assert.Equal(3, p.Order.Count);
        Assert.Equal(s1b1, p.Order[^1]);

        var all = new RecalcPlanner(g).PlanAll(formulas);
        Assert.Equal(new HashSet<long> { s1a1, dataA1 }, all.CycleMembers);
    }

    [Fact]
    public void Planner_ExtraRoots_AreEvaluatedWithDependents()
    {
        var g = new DependencyGraph();
        var resolve = Resolver(Sheets(("Sheet1", 0)));
        long a = G(0, "A1"), b = G(0, "B1");
        g.SetDependencies(a, Deps(cells: [new("Data", K("A1"))]), resolve);
        g.SetDependencies(b, Deps(cells: [new(null, K("A1"))]), resolve);
        var p = new RecalcPlanner(g).Plan([], [], [a, a], _ => true);
        Assert.Equal([a, b], p.Order);
    }

    [Fact]
    public void Workbook_QualifiedSheet1Refs_RecalcLikeUnqualified()
    {
        var wb = new Workbook();
        wb.SetCell("B1", "=sheet1!A1+SUM(SHEET1!A1:A3)");
        wb.SetCell("A2", "2");
        Assert.Equal(2.0, wb.GetValue("B1").Number);
        wb.SetCell("A1", "5");
        Assert.Equal(12.0, wb.GetValue("B1").Number);
        Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount);
    }

    [Fact]
    public void Extractor_EmitsSheetsForRefsAndRanges()
    {
        var arity = new FunctionArityAdapter(FunctionRegistry.CreateDefault());
        var deps = InputInterpreter.Interpret("=Data!A1+B2+SUM('My Sheet'!C1:C3,D1:D2)+Rate", arity)!.Deps!;
        Assert.Equal(
            new HashSet<(string?, int)> { ("Data", K("A1")), (null, K("B2")) },
            deps.Cells.Select(c => (c.Sheet, c.Key)).ToHashSet());
        Assert.Equal(
            new HashSet<(string?, string)> { ("My Sheet", "C1:C3"), (null, "D1:D2") },
            deps.Ranges.Select(r => (r.Sheet, r.Range.ToString())).ToHashSet());
        Assert.Equal(["RATE"], deps.Names);
    }
}
