using System.Diagnostics;
using System.Globalization;
using Gridline.Tests.Shared;
using Xunit.Abstractions;

namespace Gridline.Core.Tests.Performance;

/// <summary>§12 performance budgets P-2, P-3, P-4, P-7 (P-1 and P-5 live in Gridline.IO.Tests). Release build.</summary>
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
    public void P7_ManySums()
    {
        List<KeyValuePair<string, string>>? cells = new(101_000);
        for (int i = 1; i <= 100_000; i++) cells.Add(new("A" + S(i), S(i)));
        for (int i = 1; i <= 1000; i++) cells.Add(new("B" + S(i), "=SUM($A$1:$A$100000)"));

        Workbook? wb = null;
        var t = PerfHarness.Median(() => wb = null, () => wb = Snapshot(cells!));
        cells = null;
        PerfHarness.FullGc();
        long privateBytes = Process.GetCurrentProcess().PrivateMemorySize64;
        output.WriteLine($"P-7 load median {PerfHarness.Ms(t)}, private memory {privateBytes / (1024 * 1024)} MB");

        Assert.NotNull(wb);
        Assert.Equal(1000, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal("N:5000050000", wb.GetValue("B1000").ToCanonicalString());
        Assert.True(t <= TimeSpan.FromSeconds(5), $"P-7 load median {PerfHarness.Ms(t)} > 5 s");
        Assert.True(privateBytes <= 600L * 1024 * 1024, $"P-7 private memory {privateBytes / (1024 * 1024)} MB > 600 MB");
    }
}
