using Gridline.App;
using Gridline.Core;

namespace Gridline.App.Tests;

public class RecoveryServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gridline-rec-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static Workbook Dirty()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1");
        return wb;
    }

    [Fact]
    public void Autosave_OnlyWhenDirty()
    {
        var svc = new RecoveryService(_dir, Guid.NewGuid());
        var wb = new Workbook();
        svc.AutosaveIfDirty(wb);
        Assert.False(File.Exists(svc.RecoveryPath));
        wb.SetCell("A1", "1");
        svc.AutosaveIfDirty(wb);
        Assert.True(File.Exists(svc.RecoveryPath));
    }

    [Fact]
    public void RecoveryPath_IsSessionGuidInDir() =>
        Assert.Equal(Path.Combine(_dir, "0f8fad5b-d9cb-469f-a165-70867728950e.gridline"),
            new RecoveryService(_dir, Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")).RecoveryPath);

    [Fact]
    public void Find_ExcludesOwnFile()
    {
        var a = new RecoveryService(_dir, Guid.NewGuid());
        var b = new RecoveryService(_dir, Guid.NewGuid());
        a.AutosaveIfDirty(Dirty());
        b.AutosaveIfDirty(Dirty());
        Assert.Equal([b.RecoveryPath], a.FindRecoveryFiles());
        Assert.Equal([a.RecoveryPath], b.FindRecoveryFiles());
    }

    [Fact]
    public void Find_MissingDir_IsEmpty() =>
        Assert.Empty(new RecoveryService(Path.Combine(_dir, "nope"), Guid.NewGuid()).FindRecoveryFiles());

    [Fact]
    public void Find_IgnoresOtherExtensions()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "x.txt"), "x");
        Assert.Empty(new RecoveryService(_dir, Guid.NewGuid()).FindRecoveryFiles());
    }

    [Fact]
    public void Delete_RemovesFile_AndIsIdempotent()
    {
        var svc = new RecoveryService(_dir, Guid.NewGuid());
        svc.AutosaveIfDirty(Dirty());
        svc.Delete();
        Assert.False(File.Exists(svc.RecoveryPath));
        svc.Delete();
    }

    [Fact]
    public void Autosave_Session_CoversRecoveredDirtyDocument()
    {
        var src = new DocumentSession();
        src.Workbook.SetCell("A1", "9");
        Directory.CreateDirectory(_dir);
        var p = Path.Combine(_dir, "orig.gridline");
        src.SaveAs(p);
        var s = new DocumentSession();
        Assert.True(s.TryOpenRecovered(p, out _));
        var svc = new RecoveryService(_dir, s.SessionId);
        svc.AutosaveIfDirty(s);
        Assert.True(File.Exists(svc.RecoveryPath));
    }

    [Fact]
    public void ResolveDir_HonoursEnvVar()
    {
        var old = Environment.GetEnvironmentVariable(RecoveryService.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(RecoveryService.EnvVar, _dir);
            Assert.Equal(_dir, RecoveryService.ResolveDir());
            Environment.SetEnvironmentVariable(RecoveryService.EnvVar, null);
            Assert.Equal(RecoveryService.DefaultDir, RecoveryService.ResolveDir());
        }
        finally { Environment.SetEnvironmentVariable(RecoveryService.EnvVar, old); }
    }
}
