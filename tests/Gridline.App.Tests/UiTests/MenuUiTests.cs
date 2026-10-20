using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Gridline.App.Tests.UiTests;

[Collection("UI")]
[Trait("Category", "UI")]
public class MenuUiTests
{
    /// <summary>Opens the Edit menu with a real click and invokes one item (menu popups may live outside the window tree).</summary>
    internal static void InvokeEditItem(AppFixture app, string itemId)
    {
        var item = Retry.WhileNull(() =>
        {
            app.Find("MenuEdit").Click();
            var found = app.MainWindow.FindFirstDescendant(c => c.ByAutomationId(itemId))
                        ?? app.Desktop.FindFirstDescendant(c => c.ByAutomationId(itemId));
            if (found is null) Keyboard.Type(VirtualKeyShort.ESCAPE);
            return found;
        }, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(300)).Result
            ?? throw new InvalidOperationException("Menu item " + itemId + " not found after opening Edit");
        if (item.Patterns.Invoke.IsSupported) item.Patterns.Invoke.Pattern.Invoke();
        else item.Click();
    }

    [Fact]
    public void UndoRedoMenus()
    {
        using var app = new AppFixture();
        app.SetCell("A1", "7", "N:7");

        InvokeEditItem(app, "MenuUndo");
        app.WaitText("SelectionValue", t => t == "BLANK", "BLANK after undo");
        app.WaitTitle("Gridline - Untitled");

        InvokeEditItem(app, "MenuRedo");
        app.WaitText("SelectionValue", t => t == "N:7", "N:7 after redo");
    }

    [Fact]
    public void FileMenu_ItemsExist()
    {
        using var app = new AppFixture();
        app.Find("MenuEdit");
        app.Find("MenuFile").Click();
        try
        {
            Assert.NotNull(Retry.WhileNull(() => app.Desktop.FindFirstDescendant(c => c.ByAutomationId("MenuOpen")),
                TimeSpan.FromSeconds(5)).Result);
            Assert.NotNull(app.Desktop.FindFirstDescendant(c => c.ByAutomationId("MenuSave")));
        }
        finally { Keyboard.Type(VirtualKeyShort.ESCAPE); }
    }
}
