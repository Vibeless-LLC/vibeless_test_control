using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Gridline.App.Tests.UiTests;

[CollectionDefinition("UI", DisableParallelization = true)]
public sealed class UiCollection;

[Collection("UI")]
[Trait("Category", "UI")]
public class EditingUiTests
{
    [Fact]
    public void Title_UntitledThenDirty()
    {
        using var app = new AppFixture();
        app.WaitTitle("Gridline - Untitled");
        app.FocusGrid();
        Keyboard.Type("5");
        AppFixture.PressEnter();
        app.WaitTitle("Gridline - Untitled *");
    }

    [Fact]
    public void TypeCommitMovesDown()
    {
        using var app = new AppFixture();
        app.FocusGrid();
        Keyboard.Type("5");
        AppFixture.PressEnter();
        app.WaitText("NameBox", t => t == "A2", "A2");
        app.FocusGrid();
        ExtKeys.ArrowUp();
        app.WaitText("NameBox", t => t == "A1", "A1");
        app.WaitText("SelectionValue", t => t == "N:5", "N:5");
    }

    [Fact]
    public void NameBox_AcceptsAbsoluteLowercase()
    {
        using var app = new AppFixture();
        app.SubmitBox("NameBox", "$b$7");
        app.WaitText("NameBox", t => t == "B7", "B7");

        app.FocusGrid();
        app.SubmitBox("NameBox", "A0");
        // Invalid input reverts the box to the active cell.
        app.WaitText("NameBox", t => t == "B7", "B7 (reverted)");
    }

    [Fact]
    public void FormulaBar_ShowsInputAndCommits()
    {
        using var app = new AppFixture();
        app.SetCell("A1", "=1+2", "N:3");
        app.GoTo("A1");
        app.WaitText("FormulaBar", t => t == "=1+2", "=1+2");

        app.SubmitBox("FormulaBar", "42");
        app.WaitText("SelectionValue", t => t == "N:42", "N:42");
        app.WaitText("FormulaBar", t => t == "42", "42");
    }

    [Fact]
    public void SelectionSum()
    {
        using var app = new AppFixture();
        app.SetCell("A1", "1", "N:1");
        app.SetCell("A2", "2", "N:2");
        app.SetCell("A3", "x", "T:x");
        app.GoTo("A1");
        ExtKeys.ShiftDown(2);
        app.WaitText("SelectionSum", t => t.Contains('3'), "contains 3");
    }

    [Fact]
    public void Delete_ClearsSelection_OneUndo()
    {
        using var app = new AppFixture();
        app.SetCell("A1", "1", "N:1");
        app.SetCell("A2", "2", "N:2");
        app.SetCell("A3", "3", "N:3");
        app.GoTo("A1");
        ExtKeys.ShiftDown(2);
        app.WaitText("SelectionSum", t => t == "6", "6");

        Keyboard.Press(VirtualKeyShort.DELETE);
        Keyboard.Release(VirtualKeyShort.DELETE);
        app.WaitText("SelectionSum", t => t == "0", "0 after Delete");

        MenuUiTests.InvokeEditItem(app, "MenuUndo"); // one undo restores all three cells
        app.WaitText("SelectionSum", t => t == "6", "6 after one undo");
    }
}
