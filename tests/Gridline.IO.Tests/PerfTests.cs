using System.Text;
using Gridline.Core;
using Gridline.Tests.Shared;
using Xunit.Abstractions;

namespace Gridline.IO.Tests;

/// <summary>§12 performance budgets P-1 and P-5 (the others live in Gridline.Core.Tests). Release build.</summary>
[Collection(PerfCollection.Name)]
[Trait("Category", "Perf")]
public class PerfTests(ITestOutputHelper output)
{
    /// <summary>The P-1 workbook as .gridline JSON bytes, written by hand (not via the writer under test).</summary>
    private static byte[] P1Json()
    {
        var sb = new StringBuilder("{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {},\n  \"cells\": [\n");
        var cells = PerfWorkloads.P1Cells();
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
