using Gridline.App;

namespace Gridline.App.Tests;

public class CsvSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gridline-csv-" + Guid.NewGuid().ToString("N"));

    public CsvSessionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class FakePrompt(UnsavedChoice choice) : IUnsavedChangesPrompt
    {
        public int Asked;
        public UnsavedChoice Ask(string documentName) { Asked++; return choice; }
    }

    [Fact]
    public void TryImportCsv_UntitledAndDirty()
    {
        var p = Path.Combine(_dir, "a.csv");
        File.WriteAllText(p, "1,2\n3,x\n");
        var s = new DocumentSession();
        Assert.True(s.TryImportCsv(p, out var err));
        Assert.Null(err);
        Assert.Null(s.FilePath);
        Assert.True(s.IsDirty);
        Assert.Equal("Gridline - Untitled *", s.Title);
        Assert.Equal("1", s.Workbook.GetInput("A1"));
        Assert.Equal("x", s.Workbook.GetInput("B2"));
    }

    [Fact]
    public void TryImportCsv_MissingFile_ReportsAndKeepsDocument()
    {
        var s = new DocumentSession();
        s.Workbook.SetCell("A1", "7");
        var before = s.Workbook;
        Assert.False(s.TryImportCsv(Path.Combine(_dir, "nope.csv"), out var err));
        Assert.False(string.IsNullOrEmpty(err));
        Assert.Same(before, s.Workbook);
    }

    [Fact]
    public void TryExportCsv_DoesNotChangeDirtyOrPath()
    {
        var s = new DocumentSession();
        s.Workbook.SetCell("A1", "5");
        var p = Path.Combine(_dir, "out.csv");
        Assert.True(s.TryExportCsv(p, out var err));
        Assert.Null(err);
        Assert.True(s.IsDirty);
        Assert.Null(s.FilePath);
        Assert.Contains("5", File.ReadAllText(p));

        var q = Path.Combine(_dir, "b.gridline");
        s.SaveAs(q);
        Assert.True(s.TryExportCsv(p, out _));
        Assert.False(s.IsDirty);
        Assert.Equal(q, s.FilePath);
    }

    [Fact]
    public void TryExportCsv_BadPath_Reports()
    {
        var s = new DocumentSession();
        Assert.False(s.TryExportCsv(Path.Combine(_dir, "no-such-dir", "x.csv"), out var err));
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void Lifecycle_ImportCsv_PromptsWhenDirty_CancelKeeps()
    {
        var p = Path.Combine(_dir, "a.csv");
        File.WriteAllText(p, "9\n");
        var s = new DocumentSession();
        s.Workbook.SetCell("A1", "1");
        var prompt = new FakePrompt(UnsavedChoice.Cancel);
        var life = new DocumentLifecycle(s, prompt, () => null, () => null, _ => { }, () => p, () => null);
        Assert.False(life.ImportCsv());
        Assert.Equal(1, prompt.Asked);
        Assert.Equal("1", s.Workbook.GetInput("A1"));
    }

    [Fact]
    public void Lifecycle_ImportCsv_DontSave_Imports_PickerCancelDoesNothing()
    {
        var p = Path.Combine(_dir, "a.csv");
        File.WriteAllText(p, "9\n");
        var s = new DocumentSession();
        s.Workbook.SetCell("A1", "1");
        string? pick = null;
        var life = new DocumentLifecycle(s, new FakePrompt(UnsavedChoice.DontSave), () => null, () => null, _ => { }, () => pick, () => null);
        Assert.False(life.ImportCsv());
        Assert.Equal("1", s.Workbook.GetInput("A1"));
        pick = p;
        Assert.True(life.ImportCsv());
        Assert.Equal("9", s.Workbook.GetInput("A1"));
    }

    [Fact]
    public void Lifecycle_ImportCsv_ErrorReported()
    {
        var errors = new List<string>();
        var s = new DocumentSession();
        var life = new DocumentLifecycle(s, new FakePrompt(UnsavedChoice.Cancel), () => null, () => null, errors.Add,
            () => Path.Combine(_dir, "missing.csv"), () => null);
        Assert.False(life.ImportCsv());
        Assert.Single(errors);
    }

    [Fact]
    public void Lifecycle_ExportCsv_NoPrompt_WritesFile()
    {
        var p = Path.Combine(_dir, "o.csv");
        var s = new DocumentSession();
        s.Workbook.SetCell("A1", "1");
        var prompt = new FakePrompt(UnsavedChoice.Cancel);
        var life = new DocumentLifecycle(s, prompt, () => null, () => null, _ => { }, () => null, () => p);
        Assert.True(life.ExportCsv());
        Assert.Equal(0, prompt.Asked);
        Assert.True(File.Exists(p));
        Assert.True(s.IsDirty);
    }
}
