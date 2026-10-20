using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Gridline.App;

/// <summary>Modal Save / Don't Save / Cancel prompt (§11.5).</summary>
public sealed class UnsavedChangesDialog : Window, IUnsavedChangesPrompt
{
    private readonly TextBlock _message;
    private UnsavedChoice _choice = UnsavedChoice.Cancel;

    public UnsavedChangesDialog(Window? owner)
    {
        Title = "Gridline";
        Owner = owner;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, "UnsavedChangesDialog");

        _message = new TextBlock { Margin = new Thickness(0, 0, 0, 16), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(MakeButton("_Save", "SaveButton", UnsavedChoice.Save, isDefault: true, isCancel: false));
        buttons.Children.Add(MakeButton("Do_n't Save", "DontSaveButton", UnsavedChoice.DontSave, isDefault: false, isCancel: false));
        buttons.Children.Add(MakeButton("Cancel", "CancelButton", UnsavedChoice.Cancel, isDefault: false, isCancel: true));
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(_message);
        root.Children.Add(buttons);
        Content = root;
    }

    private Button MakeButton(string label, string id, UnsavedChoice choice, bool isDefault, bool isCancel)
    {
        var b = new Button
        {
            Content = label, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3),
            IsDefault = isDefault, IsCancel = isCancel,
        };
        AutomationProperties.SetAutomationId(b, id);
        b.Click += (_, _) => { _choice = choice; DialogResult = choice != UnsavedChoice.Cancel; };
        return b;
    }

    public UnsavedChoice Ask(string documentName)
    {
        _message.Text = "Do you want to save changes to " + documentName + "?";
        _choice = UnsavedChoice.Cancel;
        ShowDialog();
        return _choice;
    }
}
