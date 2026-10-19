using System.Diagnostics;
using System.Globalization;
using System.Text;
using Gridline.Core;
using Gridline.Tests.Shared;
using Xunit.Abstractions;

namespace Gridline.IO.Tests;

/// <summary>§12 performance budgets P-1, P-5 and P-7 (the others live in Gridline.Core.Tests). Release build.</summary>
[Collection(PerfCollection.Name)]
[Trait("Category", "Perf")]
public class PerfTests(ITestOutputHelper output)
{
    /// <summary>The P-1 workbook as .gridline JSON bytes, written by hand (not via the writer under test).</summary>
    private static byte[] P1Json() => Json(PerfWorkloads.P1Cells());

    /// <summary>Cells as .gridline JSON bytes, written by hand. Inputs must need no JSON escaping.</summary>
    private static byte[] Json(IReadOnlyList<KeyValuePair<string, string>> cells)
    {
        var sb = new StringBuilder("{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {},\n  \"cells\": [\n");
        for (int i = 0; i < cells.Count; i++)
        {
            sb.Append("    [\"").Append(cells[i].Key).Append("\", \"").Append(cells[i].Value).Append("\"]");
            sb.Append(i + 1 < cells.Count ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Fact]
    public void P1_Load100k()
    {
        var bytes = P1Json();
        Workbook? wb = null;
        var t = PerfHarness.Median(() => wb = null, () => wb = GridlineFile.Load(bytes));
        output.WriteLine($"P-1 Load median {PerfHarness.Ms(t)}");

        Assert.NotNull(wb);
        Assert.Equal(50_000, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal("N:5", wb.GetValue("B1").ToCanonicalString());
        Assert.Equal("N:50002", wb.GetValue("B50000").ToCanonicalString());
        Assert.True(t <= TimeSpan.FromSeconds(1.5), $"P-1 median {PerfHarness.Ms(t)} > 1.5 s");
    }

    /// <summary>P-7: the file is loaded through <see cref="GridlineFile.Load"/> from its bytes, as a user would open it.</summary>
    [Fact]
    public void P7_ManySums_LoadFile()
    {
        static string S(int i) => i.ToString(CultureInfo.InvariantCulture);
        var cells = new List<KeyValuePair<string, string>>(101_000);
        for (int i = 1; i <= 100_000; i++) cells.Add(new("A" + S(i), S(i)));
        for (int i = 1; i <= 1000; i++) cells.Add(new("B" + S(i), "=SUM($A$1:$A$100000)"));
        byte[] bytes = Json(cells);
        cells.Clear();

        Workbook? wb = null;
        var t = PerfHarness.Median(() => wb = null, () => wb = GridlineFile.Load(bytes));
        bytes = [];
        PerfHarness.FullGc();
        long privateBytes = Process.GetCurrentProcess().PrivateMemorySize64;
        output.WriteLine($"P-7 GridlineFile.Load median {PerfHarness.Ms(t)}, private memory {privateBytes / (1024 * 1024)} MB");

        Assert.NotNull(wb);
        Assert.Equal(1000, wb.LastRecalc.EvaluatedFormulaCount);
        Assert.Equal("N:5000050000", wb.GetValue("B1000").ToCanonicalString());
        Assert.True(t <= TimeSpan.FromSeconds(5), $"P-7 load median {PerfHarness.Ms(t)} > 5 s");
        Assert.True(privateBytes <= 600L * 1024 * 1024, $"P-7 private memory {privateBytes / (1024 * 1024)} MB > 600 MB");
    }

    [Fact]
    public void P5_Save()
    {
        var wb = Workbook.FromSnapshot(PerfWorkloads.P1Cells(), []);
        var dir = Directory.CreateTempSubdirectory("gridline-perf-");
        try
        {
            var path = Path.Combine(dir.FullName, "p5.gridline");
            var t = PerfHarness.Median(() => { }, () => GridlineFile.SaveToPath(wb, path));
            output.WriteLine($"P-5 SaveToPath median {PerfHarness.Ms(t)}");

            Assert.Equal(100_000, GridlineFile.LoadFromPath(path).GetUsedAddresses().Count);
            Assert.True(t <= TimeSpan.FromSeconds(1), $"P-5 median {PerfHarness.Ms(t)} > 1 s");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
