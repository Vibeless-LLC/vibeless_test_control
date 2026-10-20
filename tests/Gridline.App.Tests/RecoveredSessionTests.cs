using Gridline.App;

namespace Gridline.App.Tests;

public class RecoveredSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gridline-recs-" + Guid.NewGuid().ToString("N"));

    public RecoveredSessionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string MakeFile()
    {
        var src = new DocumentSession();
        src.Workbook.SetCell("A1", "42");
        var p = Path.Combine(_dir, "r.gridline");
        src.SaveAs(p);
        return p;
    }

    [Fact]
    public void Recovered_IsUntitledAndDirty_WithStar()
    {
        var s = new DocumentSession();
        Assert.True(s.TryOpenRecovered(MakeFile(), out var err));
        Assert.Null(err);
        Assert.Null(s.FilePath);
        Assert.True(s.IsDirty);
        Assert.False(s.Workbook.IsDirty);
        Assert.Equal("Gridline - Untitled *", s.Title);
        Assert.Equal("42", s.Workbook.GetInput("A1"));
    }

    [Fact]
    public void Recovered_SaveAs_ClearsDirty_AndRaisesSaved()
    {
        var s = new DocumentSession();
        s.TryOpenRecovered(MakeFile(), out _);
        int saved = 0;
        s.Saved += (_, _) => saved++;
        s.SaveAs(Path.Combine(_dir, "out.gridline"));
        Assert.False(s.IsDirty);
        Assert.Equal(1, saved);
        Assert.Equal("Gridline - out.gridline", s.Title);
    }

    [Fact]
    public void Recovered_NewClearsFlag()
    {
        var s = new DocumentSession();
        s.TryOpenRecovered(MakeFile(), out _);
        s.New();
        Assert.False(s.IsDirty);
    }

    [Fact]
    public void Recovered_Corrupt_ReportsAndKeepsSession()
    {
        var bad = Path.Combine(_dir, "bad.gridline");
        File.WriteAllText(bad, "garbage");
        var s = new DocumentSession();
        s.Workbook.SetCell("B1", "1");
        Assert.False(s.TryOpenRecovered(bad, out var err));
        Assert.False(string.IsNullOrEmpty(err));
        Assert.Equal("1", s.Workbook.GetInput("B1"));
        Assert.Equal("Gridline - Untitled *", s.Title);
    }

    [Fact]
    public void Save_RaisesSaved_ButFailedSaveDoesNot()
    {
        var s = new DocumentSession();
        int saved = 0;
        s.Saved += (_, _) => saved++;
        s.Workbook.SetCell("A1", "1");
        Assert.False(s.TrySaveAs(Path.Combine(_dir, "missing", "x.gridline"), out _));
        Assert.Equal(0, saved);
        Assert.True(s.TrySaveAs(Path.Combine(_dir, "ok.gridline"), out _));
        Assert.Equal(1, saved);
    }
}
