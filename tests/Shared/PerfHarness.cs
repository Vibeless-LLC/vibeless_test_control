// Linked into Gridline.Core.Tests and Gridline.IO.Tests (§12 perf harness, Task 14).
using System.Diagnostics;
using System.Runtime;

namespace Gridline.Tests.Shared;

internal static class PerfHarness
{
    /// <summary>
    /// One warm-up run, then the median of <paramref name="runs"/> timed runs (§12). <paramref name="setup"/>
    /// runs before every run, outside the timing, followed by a full GC so collection of setup garbage is not timed.
    /// </summary>
    public static TimeSpan Median(Action setup, Action measured, int runs = 5)
    {
        setup();
        measured();
        var times = new TimeSpan[runs];
        for (int i = 0; i < runs; i++)
        {
            setup();
            FullGc();
            var sw = Stopwatch.StartNew();
            measured();
            sw.Stop();
            times[i] = sw.Elapsed;
        }
        Array.Sort(times);
        return times[runs / 2];
    }

    public static void FullGc()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public static string Ms(TimeSpan t) => t.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " ms";
}

internal static class PerfWorkloads
{
    /// <summary>
    /// The P-1 workbook, row-major: A1:A50000 hold the numbers 1..50000; B{i} is <c>=A{i}+A{i+1}*2</c>, and
    /// B50000 wraps to <c>=A50000+A1*2</c>. 100,000 cells.
    /// </summary>
    public static List<KeyValuePair<string, string>> P1Cells()
    {
        const int n = 50_000;
        var cells = new List<KeyValuePair<string, string>>(2 * n);
        for (int i = 1; i <= n; i++)
        {
            string s = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string next = (i == n ? 1 : i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            cells.Add(new("A" + s, s));
            cells.Add(new("B" + s, "=A" + s + "+A" + next + "*2"));
        }
        return cells;
    }
}

/// <summary>
/// Cross-process lock held while a test assembly's perf collection runs, so perf tests in different test
/// assemblies (which <c>dotnet test</c> runs in parallel processes) never overlap.
/// </summary>
public sealed class PerfLock : IDisposable
{
    private readonly FileStream _lock;

    public PerfLock()
    {
        var path = Path.Combine(Path.GetTempPath(), "gridline-perf.lock");
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (true)
        {
            try
            {
                _lock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
        }
    }

    public void Dispose() => _lock.Dispose();
}

/// <summary>Perf tests run in this collection: serially, after all parallel collections, under <see cref="PerfLock"/>.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerfCollection : ICollectionFixture<PerfLock>
{
    public const string Name = "Perf";
}
