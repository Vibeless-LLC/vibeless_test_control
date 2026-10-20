using static Gridline.Cli.Tests.CliAppTests;

namespace Gridline.Cli.Tests;

public class ScriptPhase2Tests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gridline-p2-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }


    [Fact] public void UndoRedo() => Assert.Equal("NOOP\nOK\nA1\tBLANK\nOK\nA1\tN:1\n", Run("UNDO\nSET A1 1\nUNDO\nGET A1\nREDO\nGET A1\n","script").stdout);
    [Fact] public void RedoNoop() => Assert.Equal("NOOP\n", Run("REDO\n","script").stdout);
    [Fact] public void BeginEnd_OneStep() => Assert.Equal("OK\nA1\tBLANK\nB1\tBLANK\n", Run("BEGIN\nSET A1 1\nSETX B1 a\\tb\nEND\nUNDO\nGET A1\nGET B1\n","script").stdout);
    [Fact] public void Begin_OtherCommandErr() { var r = Run("BEGIN\nGET A1\nSET A1 1\nEND\nGET A1\n","script"); Assert.Equal(4, r.code); Assert.StartsWith("ERR\t", r.stdout); Assert.EndsWith("A1\tN:1\n", r.stdout); }
    [Fact] public void Begin_UndoDirtyRedoPasteErr_NotExecuted()
    {
        var r = Run("SET A1 1\nBEGIN\nUNDO\nREDO\nDIRTY\nPASTE A1 B1\nEND\nGET A1\nGET B1\n","script");
        Assert.Equal(4, r.code);
        Assert.Equal(4, r.stdout.Split('\n').Count(l => l.StartsWith("ERR\t")));
        Assert.EndsWith("A1\tN:1\nB1\tBLANK\n", r.stdout);
    }
    private static int ErrCount(string stdout) => stdout.Split('\n').Count(l => l.StartsWith("ERR\t"));
    [Fact] public void Begin_InvalidAddress_Atomic_OneErrAtEnd()
    {
        var r = Run("BEGIN\nSET A0 1\nSET A9 9\nEND\nGET A9\n","script");
        Assert.Equal(4, r.code);
        Assert.Equal(1, ErrCount(r.stdout));
        Assert.EndsWith("A9\tBLANK\n", r.stdout);
    }
    [Fact] public void Begin_OverlongSetx_Atomic()
    {
        var r = Run("BEGIN\nSET A1 1\nSETX B1 " + new string('a', 40000) + "\nEND\nGET A1\n","script");
        Assert.Equal(4, r.code);
        Assert.Equal(1, ErrCount(r.stdout));
        Assert.EndsWith("A1\tBLANK\n", r.stdout);
    }
    [Fact] public void Begin_BareSet_PoisonsBatch()
    {
        var r = Run("BEGIN\nSET A1 1\nSET\nEND\nGET A1\n","script");
        Assert.Equal(4, r.code);
        Assert.Equal(1, ErrCount(r.stdout));
        Assert.EndsWith("A1\tBLANK\n", r.stdout);
    }
    [Fact] public void Begin_ValidBatch_StillOneUndoStep() => Assert.Equal("OK\nNOOP\nA1\tBLANK\nB1\tBLANK\n", Run("BEGIN\nSET A1 1\nSET B1 2\nEND\nUNDO\nUNDO\nGET A1\nGET B1\n","script").stdout);
    [Fact] public void End_WithoutBegin_Err() { var r = Run("END\n","script"); Assert.Equal(4, r.code); Assert.StartsWith("ERR\t", r.stdout); }
    [Fact] public void Begin_WhileOpen_Err() { var r = Run("BEGIN\nBEGIN\nSET A1 1\nEND\nGET A1\n","script"); Assert.Equal(4, r.code); Assert.StartsWith("ERR\t", r.stdout); Assert.EndsWith("A1\tN:1\n", r.stdout); }
    [Fact] public void Begin_Unterminated_DiscardedWithErr()
    {
        var r = Run("BEGIN\nSET A1 1\n","script");
        Assert.Equal(4, r.code);
        Assert.Equal("ERR\tunterminated BEGIN\n", r.stdout);
    }
    [Fact] public void Dirty_And_SaveMarks()
    {
        var p = Path.Combine(_dir, "d.gridline");
        var r = Run("DIRTY\nSET A1 1\nDIRTY\nSAVE " + p + "\nDIRTY\nUNDO\nDIRTY\n","script");
        Assert.Equal("FALSE\nTRUE\nFALSE\nOK\nTRUE\n", r.stdout);
    }
    [Fact] public void Paste() => Assert.Equal("B2\t=B1+1\n", Run("SET A1 =A0+1\nSET A2 =A1+1\nPASTE A2 B2\nINPUT B2\n","script").stdout);
    [Fact] public void Paste_BadArgs_Err()
    {
        var r = Run("PASTE A1\nPASTE A1:B1 ZZ9\nPASTE A1 B0\n","script");
        Assert.Equal(4, r.code);
        Assert.Equal(3, r.stdout.Split('\n').Count(l => l.StartsWith("ERR\t")));
    }
}
