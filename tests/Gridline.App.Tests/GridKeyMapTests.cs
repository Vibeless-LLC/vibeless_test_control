using System.Windows.Input;
using Gridline.App;

namespace Gridline.App.Tests;

public class GridKeyMapTests
{
    [Theory]
    [InlineData(Key.Up, ModifierKeys.None, -1, 0, false)]
    [InlineData(Key.Down, ModifierKeys.None, 1, 0, false)]
    [InlineData(Key.Left, ModifierKeys.None, 0, -1, false)]
    [InlineData(Key.Right, ModifierKeys.None, 0, 1, false)]
    [InlineData(Key.Down, ModifierKeys.Shift, 1, 0, true)]
    [InlineData(Key.Left, ModifierKeys.Shift, 0, -1, true)]
    [InlineData(Key.Enter, ModifierKeys.None, 1, 0, false)]
    [InlineData(Key.Enter, ModifierKeys.Shift, -1, 0, false)]
    [InlineData(Key.Tab, ModifierKeys.None, 0, 1, false)]
    [InlineData(Key.Tab, ModifierKeys.Shift, 0, -1, false)]
    public void Navigation_Moves(Key key, ModifierKeys mods, int dRow, int dCol, bool extend) =>
        Assert.Equal(new GridKeyAction(GridKeyCommand.Move, dRow, dCol, extend), GridKeyMap.MapGridKey(key, mods));

    [Fact]
    public void Home_And_CtrlHome_MoveToEdges()
    {
        var home = GridKeyMap.MapGridKey(Key.Home, ModifierKeys.None);
        Assert.Equal(GridKeyCommand.Move, home.Command);
        Assert.Equal(0, home.DRow);
        Assert.True(home.DCol <= -702);
        var top = GridKeyMap.MapGridKey(Key.Home, ModifierKeys.Control);
        Assert.True(top.DRow <= -100000 && top.DCol <= -702);
    }

    [Fact]
    public void Page_F2_Delete()
    {
        Assert.Equal(new GridKeyAction(GridKeyCommand.Page, 1, 0, false), GridKeyMap.MapGridKey(Key.PageDown, ModifierKeys.None));
        Assert.Equal(new GridKeyAction(GridKeyCommand.Page, -1, 0, true), GridKeyMap.MapGridKey(Key.PageUp, ModifierKeys.Shift));
        Assert.Equal(GridKeyCommand.EditExisting, GridKeyMap.MapGridKey(Key.F2, ModifierKeys.None).Command);
        Assert.Equal(GridKeyCommand.Clear, GridKeyMap.MapGridKey(Key.Delete, ModifierKeys.None).Command);
    }

    [Theory]
    [InlineData(Key.Z, ModifierKeys.Control)]
    [InlineData(Key.Down, ModifierKeys.Control)]
    [InlineData(Key.Delete, ModifierKeys.Shift)]
    [InlineData(Key.A, ModifierKeys.None)]
    [InlineData(Key.Enter, ModifierKeys.Alt)]
    public void Others_AreNotHandled(Key key, ModifierKeys mods) =>
        Assert.Equal(GridKeyCommand.None, GridKeyMap.MapGridKey(key, mods).Command);

    [Theory]
    [InlineData(Key.Enter, ModifierKeys.None, EditorKeyCommand.Commit, 1, 0)]
    [InlineData(Key.Enter, ModifierKeys.Shift, EditorKeyCommand.Commit, -1, 0)]
    [InlineData(Key.Tab, ModifierKeys.None, EditorKeyCommand.Commit, 0, 1)]
    [InlineData(Key.Tab, ModifierKeys.Shift, EditorKeyCommand.Commit, 0, -1)]
    [InlineData(Key.Escape, ModifierKeys.None, EditorKeyCommand.Cancel, 0, 0)]
    [InlineData(Key.Left, ModifierKeys.None, EditorKeyCommand.None, 0, 0)]
    [InlineData(Key.A, ModifierKeys.None, EditorKeyCommand.None, 0, 0)]
    public void EditorKeys(Key key, ModifierKeys mods, EditorKeyCommand cmd, int dRow, int dCol) =>
        Assert.Equal(new EditorKeyAction(cmd, dRow, dCol), GridKeyMap.MapEditorKey(key, mods));

    [Theory]
    [InlineData("5", true)]
    [InlineData("=", true)]
    [InlineData(" ", true)]
    [InlineData("é", true)]
    [InlineData("", false)]
    [InlineData("\r", false)]
    [InlineData("\t", false)]
    [InlineData("\u001a", false)] // Ctrl+Z
    [InlineData("\u001b", false)] // Esc
    [InlineData("\u007f", false)]
    public void IsPrintable(string text, bool expected) => Assert.Equal(expected, GridKeyMap.IsPrintable(text));
}
