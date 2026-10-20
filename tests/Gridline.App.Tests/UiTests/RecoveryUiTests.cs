using FlaUI.Core.Tools;

namespace Gridline.App.Tests.UiTests;

[Collection("UI")]
[Trait("Category", "UI")]
public class RecoveryUiTests
{
    [Fact]
    public void RecoveryDialog_ShownWhenFilesExist()
    {
        const string json = "{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {},\n  \"cells\": [\n    [\"A1\", \"5\"]\n  ]\n}\n";
        using var app = new AppFixture(beforeLaunch: dir =>
            File.WriteAllText(Path.Combine(dir, Guid.NewGuid().ToString("D") + ".gridline"), json));
        var dlg = Retry.WhileNull(() => app.MainWindow.ModalWindows.FirstOrDefault(w => w.AutomationId == "RecoveryDialog")
                                        ?? app.Desktop.FindFirstChild(c => c.ByAutomationId("RecoveryDialog")),
            TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(100)).Result;
        Assert.NotNull(dlg);
    }

    [Fact]
    public void RecoveryDialog_NotShownWithoutFiles()
    {
        using var app = new AppFixture();
        app.FocusGrid();
        Assert.Empty(app.MainWindow.ModalWindows);
    }
}
