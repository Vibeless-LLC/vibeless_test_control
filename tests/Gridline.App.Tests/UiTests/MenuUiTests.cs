using System.Runtime.InteropServices;
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

    [Fact]
    public void ImportCsv_MenuExists()
    {
        using var app = new AppFixture();
        app.Find("MenuEdit");
        app.Find("MenuFile").Click();
        try
        {
            Assert.NotNull(Retry.WhileNull(() => app.Desktop.FindFirstDescendant(c => c.ByName("Import CSV...")),
                TimeSpan.FromSeconds(5)).Result);
            Assert.NotNull(app.Desktop.FindFirstDescendant(c => c.ByName("Export CSV...")));
            Assert.NotNull(app.Desktop.FindFirstDescendant(c => c.ByAutomationId("MenuImportCsv")));
            Assert.NotNull(app.Desktop.FindFirstDescendant(c => c.ByAutomationId("MenuExportCsv")));
        }
        finally { Keyboard.Type(VirtualKeyShort.ESCAPE); }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [Fact]
    public void RowHeader_InsertShiftsData()
    {
        using var app = new AppFixture();
        app.SetCell("A1", "5", "N:5");

        // Row 1's header sits at the grid's left edge, just below the column header (sizes in DIPs, scaled to pixels).
        double scale = GetDpiForWindow(app.MainWindow.Properties.NativeWindowHandle.Value) / 96.0;
        var rect = app.Find("Grid").BoundingRectangle;
        var point = new System.Drawing.Point(
            (int)(rect.Left + 28 * scale),   // GridControl.RowHeaderWidth / 2
            (int)(rect.Top + 33 * scale));   // HeaderHeight + RowHeight / 2
        Mouse.RightClick(point);

        var insert = Retry.WhileNull(() => app.Desktop.FindFirstDescendant(c => c.ByAutomationId("HeaderInsert")),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100)).Result
            ?? throw new InvalidOperationException("Insert item not found in the header context menu");
        if (insert.Patterns.Invoke.IsSupported) insert.Patterns.Invoke.Pattern.Invoke();
        else insert.Click();

        app.GoTo("A2");
        app.WaitText("SelectionValue", t => t == "N:5", "N:5 after row insert");
    }
}
