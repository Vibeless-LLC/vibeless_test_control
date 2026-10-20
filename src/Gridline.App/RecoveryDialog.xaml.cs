using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Gridline.App;

/// <summary>
/// Lists recovery files with Restore and Discard buttons (§11.6). <c>restore</c> returns an error message, or null on
/// success (the caller deletes the file); <c>discard</c> deletes a file. A failed restore offers to discard the file.
/// One window holds one document, so after a successful restore the other Restore buttons are disabled.
/// </summary>
public partial class RecoveryDialog : Window
{
    private readonly Func<string, string?> _restore;
    private readonly Action<string> _discard;
    private bool _restored;

    public RecoveryDialog(IReadOnlyList<string> files, Func<string, string?> restore, Action<string> discard)
    {
        _restore = restore;
        _discard = discard;
        InitializeComponent();
        foreach (var f in files) AddRow(f);
    }

    private void AddRow(string path)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };
        var discard = new Button { Content = "Discard", Margin = new Thickness(8, 0, 0, 0), MinWidth = 70 };
        var restore = new Button { Content = "Restore", Margin = new Thickness(8, 0, 0, 0), MinWidth = 70, IsEnabled = !_restored };
        AutomationProperties.SetAutomationId(restore, "RestoreButton");
        AutomationProperties.SetAutomationId(discard, "DiscardButton");
        DockPanel.SetDock(discard, Dock.Right);
        DockPanel.SetDock(restore, Dock.Right);
        string when = File.Exists(path) ? File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss") : "";
        var label = new TextBlock
        {
            Text = Path.GetFileName(path) + "  (" + when + ")",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.Children.Add(discard);
        row.Children.Add(restore);
        row.Children.Add(label);
        discard.Click += (_, _) => { _discard(path); RemoveRow(row); };
        restore.Click += (_, _) => OnRestore(path, row);
        FileList.Children.Add(row);
    }

    private void OnRestore(string path, DockPanel row)
    {
        string? error = _restore(path);
        if (error is null)
        {
            _restored = true;
            RemoveRow(row);
            foreach (var other in FileList.Children.OfType<DockPanel>())
                foreach (var b in other.Children.OfType<Button>())
                    if ((string)b.Content == "Restore") b.IsEnabled = false;
            return;
        }
        var answer = MessageBox.Show(this,
            "Could not restore " + Path.GetFileName(path) + ": " + error + "\n\nDiscard this recovery file?",
            "Gridline", MessageBoxButton.YesNo, MessageBoxImage.Error);
        if (answer == MessageBoxResult.Yes) { _discard(path); RemoveRow(row); }
    }

    private void RemoveRow(DockPanel row)
    {
        FileList.Children.Remove(row);
        if (FileList.Children.Count == 0) Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
