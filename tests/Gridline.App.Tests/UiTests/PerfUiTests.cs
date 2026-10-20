using System.Globalization;
using System.Text;
using Xunit.Abstractions;

namespace Gridline.App.Tests.UiTests;

[Collection("UI")]
[Trait("Category", "UI")]
public class PerfUiTests(ITestOutputHelper output)
{
    /// <summary>The P-1 workbook (50k numbers, 50k formulas with 2 refs each) as canonical .gridline JSON.</summary>
    internal static string WriteP1File(string dir)
    {
        const int n = 50_000;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {},\n  \"cells\": [\n");
        for (int i = 1; i <= n; i++)
        {
            string s = i.ToString(inv), next = (i == n ? 1 : i + 1).ToString(inv);
            sb.Append("    [\"A").Append(s).Append("\", \"").Append(s).Append("\"],\n");
            sb.Append("    [\"B").Append(s).Append("\", \"=A").Append(s).Append("+A").Append(next).Append("*2\"]");
            sb.Append(i < n ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        var path = Path.Combine(dir, "p6.gridline");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void P6_Open100kFile_RenderedWithin3s()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gridline-p6-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = WriteP1File(dir);
            var times = new List<double>();
            for (int run = 0; run < 3; run++)
            {
                // Measured from just before process start until A1's SelectionValue reads "N:..." (document loaded, grid rendered).
                using var app = new AppFixture(file, waitForMainWindow: false);
                app.MainWindow = app.WaitForMainWindow();
                app.WaitText("SelectionValue", t => t.StartsWith("N:", StringComparison.Ordinal), "N:...", timeoutMs: 30000);
                app.SinceStart.Stop();
                times.Add(app.SinceStart.Elapsed.TotalSeconds);
                app.WaitTitle("Gridline - p6.gridline");
            }
            var runs = string.Join(", ", times.Select(t => t.ToString("F3", CultureInfo.InvariantCulture)));
            times.Sort();
            string msg = "P-6 runs (s, in order): " + runs + " | max " + times[2].ToString("F3", CultureInfo.InvariantCulture) +
                         "; median " + times[1].ToString("F3", CultureInfo.InvariantCulture);
            output.WriteLine(msg);
            Assert.True(times[1] <= 3.0 && times[2] <= 3.0, msg); // median per brief; max asserted too
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }
}
