using Gridline.App;

namespace Gridline.App.Tests;

public class DocumentSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gridline-app-" + Guid.NewGuid().ToString("N"));

    public DocumentSessionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact] public void Title_UntitledClean() => Assert.Equal("Gridline - Untitled", new DocumentSession().Title);

    [Fact]
    public void Title_DirtyStar()
    {
        var s = new DocumentSession();
        s.Workbook.SetCell("A1", "1");
        Assert.Equal("Gridline - Untitled *", s.Title);
    }

    [Fact]
    public void SaveAs_SetsNameAndClean()
    {
        var s = new DocumentSession();
        s.Workbook.SetCell("A1", "1");
        var p = Path.Combine(_dir, "book.gridline");
        s.SaveAs(p);
        Assert.Equal("Gridline - book.gridline", s.Title);
        Assert.False(s.Workbook.IsDirty);
        Assert.Equal(p, s.FilePath);
    }

    [Fact]
    public void Open_LoadsClean()
    {
        var a = new DocumentSession();
        a.Workbook.SetCell("A1", "5");
        a.Workbook.SetCell("A2", "=A1*2");
        var p = Path.Combine(_dir, "open.gridline");
        a.SaveAs(p);

        var b = new DocumentSession();
        b.Open(p);
        Assert.Equal("Gridline - open.gridline", b.Title);
        Assert.False(b.Workbook.IsDirty);
        Assert.Equal("=A1*2", b.Workbook.GetInput("A2"));
    }

    [Fact]
    public void Save_WithoutPath_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new DocumentSession().Save());

    [Fact]
    public void Save_WritesToPathAndClearsDirty()
    {
        var s = new DocumentSession();
        var p = Path.Combine(_dir, "s.gridline");
        s.SaveAs(p);
        s.Workbook.SetCell("B1", "x");
        Assert.EndsWith(" *", s.Title);
        s.Save();
        Assert.Equal("Gridline - s.gridline", s.Title);
        var reopened = new DocumentSession();
        reopened.Open(p);
        Assert.Equal("x", reopened.Workbook.GetInput("B1"));
    }

    [Fact]
    public void New_ResetsWorkbookAndPath()
    {
        var s = new DocumentSession();
        s.SaveAs(Path.Combine(_dir, "n.gridline"));
        s.Workbook.SetCell("A1", "1");
        s.New();
        Assert.Null(s.FilePath);
        Assert.Equal("Gridline - Untitled", s.Title);
        Assert.Equal("", s.Workbook.GetInput("A1"));
    }

    [Fact]
    public void StateChanged_FiresOnEditSaveNewAndOpen_AndFollowsNewWorkbook()
    {
        var s = new DocumentSession();
        var n = 0;
        s.StateChanged += (_, _) => n++;
        s.Workbook.SetCell("A1", "1");
        Assert.Equal(1, n);
        var p = Path.Combine(_dir, "e.gridline");
        s.SaveAs(p);
        Assert.Equal(2, n);
        s.New();
        Assert.Equal(3, n);
        s.Workbook.SetCell("A1", "2"); // subscribed to the new workbook
        Assert.Equal(4, n);
        s.Open(p);
        Assert.Equal(5, n);
    }

    [Fact]
    public void Undo_UpdatesTitleDirtiness()
    {
        var s = new DocumentSession();
        s.Workbook.SetCell("A1", "1");
        s.Workbook.Undo();
        Assert.Equal("Gridline - Untitled", s.Title);
    }

    [Fact]
    public void SessionId_IsUniquePerSession() =>
        Assert.NotEqual(new DocumentSession().SessionId, new DocumentSession().SessionId);
}
