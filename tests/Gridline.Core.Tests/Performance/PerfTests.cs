using System.Diagnostics;
using System.Globalization;
using Gridline.Tests.Shared;
using Xunit.Abstractions;

namespace Gridline.Core.Tests.Performance;

/// <summary>§12 performance budgets P-2, P-3, P-4 and CR1.9 P-8 (P-1, P-5 and P-7 load or save files, so they live in Gridline.IO.Tests). Release build.</summary>
[Collection(PerfCollection.Name)]
[Trait("Category", "Perf")]
public class PerfTests(ITestOutputHelper output)
{
    private static string S(long i) => i.ToString(CultureInfo.InvariantCulture);

    private static Workbook Snapshot(List<KeyValuePair<string, string>> cells) => Workbook.FromSnapshot(cells, []);

    [Fact]
    public void P2_Chain()
    {
        var cells = new List<KeyValuePair<string, string>>(100_000) { new("A1", "1") };
        for (int i = 2; i <= 100_000; i++) cells.Add(new("A" + S(i), "=A" + S(i - 1) + "+1"));
        var wb = Snapshot(cells);

        var t = PerfHarness.Median(() => wb.SetCell("A1", "1"), () => wb.SetCell("A1", "2"));
        output.WriteLine($"P-2 SetCell(A1) median {PerfHarness.Ms(t)}");

        Assert.Equal(99_999, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal("N:100001", wb.GetValue("A100000").ToCanonicalString());
        Assert.True(t <= TimeSpan.FromMilliseconds(250), $"P-2 median {PerfHarness.Ms(t)} > 250 ms");
    }

    [Fact]
    public void P3_NoDependents()
    {
        var wb = Snapshot(PerfWorkloads.P1Cells());
        wb.SetCell("C1", "0"); // warm-up
        var times = new long[1000];
        for (int i = 1; i <= 1000; i++)
        {
            string addr = "C" + S(i), input = S(i);
            long start = Stopwatch.GetTimestamp();
            wb.SetCell(addr, input);
            times[i - 1] = Stopwatch.GetTimestamp() - start;
        }
        Array.Sort(times);
        var median = Stopwatch.GetElapsedTime(0, times[500]);
        output.WriteLine($"P-3 SetCell(C{{i}}) median {PerfHarness.Ms(median)}, max {PerfHarness.Ms(Stopwatch.GetElapsedTime(0, times[^1]))}");

        Assert.Equal(0, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal("N:1000", wb.GetValue("C1000").ToCanonicalString());
        Assert.True(median <= TimeSpan.FromMilliseconds(1), $"P-3 median {PerfHarness.Ms(median)} > 1 ms");
    }

    [Fact]
    public void P4_SumEdit()
    {
        var cells = new List<KeyValuePair<string, string>>(100_001);
        for (int i = 1; i <= 100_000; i++) cells.Add(new("A" + S(i), S(i)));
        cells.Add(new("B1", "=SUM(A1:A100000)"));
        var wb = Snapshot(cells);

        var t = PerfHarness.Median(() => wb.SetCell("A500", "500"), () => wb.SetCell("A500", "7"));
        output.WriteLine($"P-4 SetCell(A500) median {PerfHarness.Ms(t)}");

        Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal("N:" + S(5_000_050_000 - 500 + 7), wb.GetValue("B1").ToCanonicalString());
        Assert.True(t <= TimeSpan.FromMilliseconds(50), $"P-4 median {PerfHarness.Ms(t)} > 50 ms");
    }

    [Fact]
    public void P8_RenameMostReferencedSheet()
    {
        // CR1.9: S1..S10; Si has numbers in A1:A10000 and =S{j}!A{r}+S1!A{r} (j = i % 10 + 1) in B1:B10000, so S1 is
        // referenced by all 100,000 formulas. "S1" looks like a cell address, so the CR1.2 rules quote it: 'S1'!A1.
        // Built through the API (the multi-sheet FromSnapshot is Task 7); setup is not timed.
        const int rows = 10_000;
        var wb = new Workbook();
        for (int i = 1; i <= 10; i++) wb.AddSheet("S" + S(i));
        wb.DeleteSheet("Sheet1");
        for (int i = 1; i <= 10; i++)
        {
            string self = "'S" + S(i) + "'!", other = "'S" + S(i % 10 + 1) + "'!";
            var cells = new List<KeyValuePair<string, string>>(2 * rows);
            for (int r = 1; r <= rows; r++)
            {
                cells.Add(new(self + "A" + S(r), S(r * i)));
                cells.Add(new(self + "B" + S(r), "=" + other + "A" + S(r) + "+'S1'!A" + S(r)));
            }
            wb.ApplyBatch(cells);
        }
        var before = new Dictionary<string, string>();
        foreach (string a in wb.GetUsedAddresses()) before[a] = wb.GetValue(a).ToCanonicalString();
        Assert.Equal(200_000, before.Count);
        Assert.Equal("N:8", before["'S2'!B2"]); // 'S3'!A2 (6) + 'S1'!A2 (2)

        bool renamed = false;
        var t = PerfHarness.Median(
            () => { },
            () =>
            {
                if (renamed) wb.RenameSheet("Renamed", "S1"); else wb.RenameSheet("S1", "Renamed");
                renamed = !renamed;
            });
        output.WriteLine($"P-8 RenameSheet(S1) median {PerfHarness.Ms(t)}");

        // 1 warm-up + 5 timed renames: an even number, so the sheet is S1 again; values are unchanged.
        Assert.False(renamed);
        Assert.Equal("='S3'!A1+'S1'!A1", wb.GetInput("'S2'!B1"));
        foreach (var (a, v) in before) Assert.Equal(v, wb.GetValue(a).ToCanonicalString());

        wb.RenameSheet("S1", "Renamed");
        Assert.Equal("='S3'!A1+Renamed!A1", wb.GetInput("'S2'!B1"));
        Assert.Equal("=Renamed!A1+Renamed!A1", wb.GetInput("'S10'!B1"));
        Assert.Equal("='S2'!A1+Renamed!A1", wb.GetInput("Renamed!B1"));
        string Now(string a) => a.StartsWith("'S1'!", StringComparison.Ordinal) ? "Renamed!" + a[5..] : a;
        foreach (var (a, v) in before) Assert.Equal(v, wb.GetValue(Now(a)).ToCanonicalString());
        wb.Undo();
        Assert.Equal("='S3'!A1+'S1'!A1", wb.GetInput("'S2'!B1"));
        foreach (var (a, v) in before) Assert.Equal(v, wb.GetValue(a).ToCanonicalString());
        Assert.True(t <= TimeSpan.FromMilliseconds(500), $"P-8 median {PerfHarness.Ms(t)} > 500 ms");
    }
}
