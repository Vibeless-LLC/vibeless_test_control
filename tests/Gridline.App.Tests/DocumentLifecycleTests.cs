using Gridline.App;

namespace Gridline.App.Tests;

public class DocumentLifecycleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gridline-life-" + Guid.NewGuid().ToString("N"));

    public DocumentLifecycleTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class FakePrompt(UnsavedChoice choice) : IUnsavedChangesPrompt
    {
        public int Asked;
        public UnsavedChoice Ask(string documentName) { Asked++; return choice; }
    }

    private sealed class Harness
    {
        public DocumentSession Session = new();
        public FakePrompt Prompt;
        public string? SavePath, OpenPath;
        public List<string> Errors = new();
        public DocumentLifecycle Life;
        public Harness(UnsavedChoice choice)
        {
            Prompt = new FakePrompt(choice);
            Life = new DocumentLifecycle(Session, Prompt, () => SavePath, () => OpenPath, Errors.Add);
        }
    }

    [Fact]
    public void Clean_NoPrompt()
    {
        var h = new Harness(UnsavedChoice.Cancel);
        Assert.True(h.Life.ConfirmDiscard());
        Assert.Equal(0, h.Prompt.Asked);
    }

    [Fact]
    public void Dirty_Cancel_Aborts_New()
    {
        var h = new Harness(UnsavedChoice.Cancel);
        h.Session.Workbook.SetCell("A1", "1");
        Assert.False(h.Life.New());
        Assert.Equal("1", h.Session.Workbook.GetInput("A1"));
    }

    [Fact]
    public void Dirty_DontSave_Proceeds()
    {
        var h = new Harness(UnsavedChoice.DontSave);
        h.Session.Workbook.SetCell("A1", "1");
        Assert.True(h.Life.New());
        Assert.False(h.Session.IsDirty);
        Assert.Equal("", h.Session.Workbook.GetInput("A1"));
    }

    [Fact]
    public void Dirty_DontSave_Exit_Proceeds()
    {
        var h = new Harness(UnsavedChoice.DontSave);
        h.Session.Workbook.SetCell("A1", "1");
        Assert.True(h.Life.Exit());
        Assert.Equal(1, h.Prompt.Asked);
    }

    [Fact]
    public void Dirty_Cancel_Exit_Aborts()
    {
        var h = new Harness(UnsavedChoice.Cancel);
        h.Session.Workbook.SetCell("A1", "1");
        Assert.False(h.Life.Exit());
    }

    [Fact]
    public void Dirty_Save_Untitled_UsesSaveAs_ThenProceeds()
    {
        var h = new Harness(UnsavedChoice.Save);
        h.SavePath = Path.Combine(_dir, "a.gridline");
        h.Session.Workbook.SetCell("A1", "1");
        Assert.True(h.Life.Exit());
        Assert.True(File.Exists(h.SavePath));
        Assert.False(h.Session.IsDirty);
    }

    [Fact]
    public void Dirty_Save_SaveAsCancelled_AbortsAction()
    {
        var h = new Harness(UnsavedChoice.Save);
        h.SavePath = null;
        h.Session.Workbook.SetCell("A1", "1");
        Assert.False(h.Life.New());
        Assert.True(h.Session.IsDirty);
        Assert.Equal("1", h.Session.Workbook.GetInput("A1"));
    }

    [Fact]
    public void Dirty_Save_Titled_SavesToSamePath()
    {
        var h = new Harness(UnsavedChoice.Save);
        var p = Path.Combine(_dir, "t.gridline");
        h.Session.SaveAs(p);
        h.Session.Workbook.SetCell("A1", "5");
        Assert.True(h.Life.ConfirmDiscard());
        Assert.False(h.Session.IsDirty);
        Assert.Null(h.SavePath);
    }

    [Fact]
    public void Dirty_Save_Failure_ReportsAndAborts()
    {
        var h = new Harness(UnsavedChoice.Save);
        h.SavePath = Path.Combine(_dir, "no-such-dir", "x.gridline");
        h.Session.Workbook.SetCell("A1", "1");
        Assert.False(h.Life.Exit());
        Assert.Single(h.Errors);
    }

    [Fact]
    public void Open_BadFile_ReportsAndKeepsDocument()
    {
        var h = new Harness(UnsavedChoice.DontSave);
        var bad = Path.Combine(_dir, "bad.gridline");
        File.WriteAllText(bad, "not a gridline file");
        h.Session.Workbook.SetCell("A1", "7");
        h.OpenPath = bad;
        Assert.False(h.Life.Open());
        Assert.Single(h.Errors);
        Assert.Equal("7", h.Session.Workbook.GetInput("A1"));
    }

    [Fact]
    public void Open_Cancelled_Picker_KeepsDocument()
    {
        var h = new Harness(UnsavedChoice.DontSave);
        h.Session.Workbook.SetCell("A1", "7");
        Assert.False(h.Life.Open());
        Assert.Equal("7", h.Session.Workbook.GetInput("A1"));
    }

    [Fact]
    public void Open_Good_Loads()
    {
        var src = new DocumentSession();
        src.Workbook.SetCell("B2", "=1+1");
        var p = Path.Combine(_dir, "g.gridline");
        src.SaveAs(p);
        var h = new Harness(UnsavedChoice.Cancel) { OpenPath = p };
        Assert.True(h.Life.Open());
        Assert.Equal("=1+1", h.Session.Workbook.GetInput("B2"));
        Assert.Empty(h.Errors);
    }
}
