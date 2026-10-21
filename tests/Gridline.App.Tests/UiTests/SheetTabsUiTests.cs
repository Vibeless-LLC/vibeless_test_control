using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Gridline.App.Tests.UiTests;

/// <summary>Sheet tab strip and qualified name box (CR1.8, §11.7).</summary>
[Collection("UI")]
[Trait("Category", "UI")]
public class SheetTabsUiTests
{
    /// <summary>Right-clicks a tab and returns a context menu item (menu popups live outside the window tree).</summary>
    private static AutomationElement OpenTabMenuItem(AppFixture app, string tabId, string itemId)
    {
        var item = Retry.WhileNull(() =>
        {
            app.MainWindow.SetForeground();
            app.Find(tabId).RightClick();
            var found = Retry.WhileNull(() => app.Desktop.FindFirstDescendant(c => c.ByAutomationId(itemId)),
                TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100)).Result;
            if (found is null) Keyboard.Type(VirtualKeyShort.ESCAPE);
            return found;
        }, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(300)).Result;
        return item ?? throw new InvalidOperationException(itemId + " not found in the sheet tab context menu");
    }

    private static void Invoke(AutomationElement item)
    {
        if (item.Patterns.Invoke.IsSupported) item.Patterns.Invoke.Pattern.Invoke();
        else item.Click();
    }

    private static void WaitTab(AppFixture app, string tabId, string name, ToggleState state)
    {
        string last = "";
        var r = Retry.WhileFalse(() =>
        {
            try
            {
                var t = app.MainWindow.FindFirstDescendant(c => c.ByAutomationId(tabId));
                if (t is null) { last = "(missing)"; return false; }
                var s = t.Patterns.Toggle.Pattern.ToggleState.Value;
                last = t.Name + "/" + s;
                return t.Name == name && s == state;
            }
            catch (Exception) { return false; }
        }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50));
        if (!r.Success) throw new Xunit.Sdk.XunitException($"{tabId}: expected {name}/{state}, last was '{last}'");
    }

    private static void AddSheetViaMenu(AppFixture app)
    {
        Invoke(OpenTabMenuItem(app, "SheetTab_0", "SheetAdd"));
        WaitTab(app, "SheetTab_1", "Sheet2", ToggleState.Off);
    }

    [Fact]
    public void Tabs_ShowSheet1()
    {
        using var app = new AppFixture();
        app.Find("SheetTabs");
        WaitTab(app, "SheetTab_0", "Sheet1", ToggleState.On);
        Assert.Null(app.MainWindow.FindFirstDescendant(c => c.ByAutomationId("SheetTab_1")));
    }

    [Fact]
    public void ContextAdd_ThenClick_SwitchesGrid()
    {
        using var app = new AppFixture();
        AddSheetViaMenu(app);
        app.WaitTitle("Gridline - Untitled *");

        app.Find("SheetTab_1").Click();
        WaitTab(app, "SheetTab_1", "Sheet2", ToggleState.On);
        WaitTab(app, "SheetTab_0", "Sheet1", ToggleState.Off);
        app.SetCell("A1", "7", "N:7"); // on Sheet2

        app.Find("SheetTab_0").Click();
        WaitTab(app, "SheetTab_0", "Sheet1", ToggleState.On);
        app.WaitText("SelectionValue", t => t == "BLANK", "BLANK on Sheet1");

        app.SubmitBox("NameBox", "Sheet2!A1");
        app.WaitText("SelectionValue", t => t == "N:7", "N:7 on Sheet2");
        WaitTab(app, "SheetTab_1", "Sheet2", ToggleState.On);
    }

    [Fact]
    public void NameBox_Qualified_SwitchesTab()
    {
        using var app = new AppFixture();
        AddSheetViaMenu(app);

        app.SubmitBox("NameBox", "sheet2!$c$3");
        app.WaitText("NameBox", t => t == "C3", "C3");
        WaitTab(app, "SheetTab_1", "Sheet2", ToggleState.On);
        WaitTab(app, "SheetTab_0", "Sheet1", ToggleState.Off);

        app.FocusGrid();
        app.SubmitBox("NameBox", "Nope!A1"); // unknown sheet: the box reverts, nothing changes
        app.WaitText("NameBox", t => t == "C3", "C3 (reverted)");
        WaitTab(app, "SheetTab_1", "Sheet2", ToggleState.On);
    }

    [Fact]
    public void Delete_DisabledForLastSheet()
    {
        using var app = new AppFixture();
        try
        {
            var delete = OpenTabMenuItem(app, "SheetTab_0", "SheetDelete");
            Assert.False(delete.IsEnabled);
            Assert.False(app.Desktop.FindFirstDescendant(c => c.ByAutomationId("SheetMoveLeft"))!.IsEnabled);
            Assert.False(app.Desktop.FindFirstDescendant(c => c.ByAutomationId("SheetMoveRight"))!.IsEnabled);
            Assert.True(app.Desktop.FindFirstDescendant(c => c.ByAutomationId("SheetAdd"))!.IsEnabled);
        }
        finally { Keyboard.Type(VirtualKeyShort.ESCAPE); }
        WaitTab(app, "SheetTab_0", "Sheet1", ToggleState.On);
    }

    [Fact]
    public void Rename_EnterCommits()
    {
        using var app = new AppFixture();
        Invoke(OpenTabMenuItem(app, "SheetTab_0", "SheetRename"));
        var box = Retry.WhileNull(() => app.Desktop.FindFirstDescendant(c => c.ByAutomationId("SheetRenameBox")),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100)).Result
            ?? throw new InvalidOperationException("SheetRenameBox not found");
        box.Focus();
        box.Patterns.Value.Pattern.SetValue("Data");
        AppFixture.PressEnter();
        WaitTab(app, "SheetTab_0", "Data", ToggleState.On);
        app.WaitTitle("Gridline - Untitled *");
    }

    [Fact]
    public void Drag_TabOntoTab_MovesSheet()
    {
        using var app = new AppFixture();
        AddSheetViaMenu(app);
        var from = app.Find("SheetTab_1").GetClickablePoint();
        var to = app.Find("SheetTab_0").GetClickablePoint();
        app.MainWindow.SetForeground();
        Mouse.MoveTo(from);
        Mouse.Down(MouseButton.Left);
        Mouse.MoveTo(new System.Drawing.Point(from.X - 10, from.Y));
        Mouse.MoveTo(new System.Drawing.Point(to.X + 5, to.Y));
        Mouse.MoveTo(to);
        Mouse.Up(MouseButton.Left);
        WaitTab(app, "SheetTab_0", "Sheet2", ToggleState.Off);
        WaitTab(app, "SheetTab_1", "Sheet1", ToggleState.On);
    }
}
