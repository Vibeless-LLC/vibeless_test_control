using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Gridline.App;

/// <summary>Small modal prompt for a new sheet name (CR1.8): Enter commits, Esc cancels.</summary>
public sealed class SheetRenameDialog : Window
{
    private readonly TextBox _box;

    public SheetRenameDialog(Window? owner, string current)
    {
        Title = "Rename Sheet";
        Owner = owner;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, "SheetRenameDialog");

        _box = new TextBox { Text = current, MinWidth = 220, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetAutomationId(_box, "SheetRenameBox");
        var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true, Padding = new Thickness(8, 3, 8, 3) };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
        ok.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Sheet name:", Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(_box);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    /// <summary>Shows the dialog; the entered name, or null when cancelled.</summary>
    public string? Ask() => ShowDialog() == true ? _box.Text : null;
}
