using System.Windows.Input;
using Gridline.Core.Addressing;

namespace Gridline.App;

public enum GridKeyCommand { None, Move, Page, EditExisting, Clear }

/// <summary>What a key does on the grid. <see cref="GridKeyCommand.Page"/> deltas are in viewports.</summary>
public readonly record struct GridKeyAction(GridKeyCommand Command, int DRow = 0, int DCol = 0, bool Extend = false);

public enum EditorKeyCommand { None, Commit, Cancel }

/// <summary>What a key does in the in-cell editor: commit (then move by the delta) or cancel.</summary>
public readonly record struct EditorKeyAction(EditorKeyCommand Command, int DRow = 0, int DCol = 0);

/// <summary>UI-free key → action mapping for the grid and the in-cell editor (§11.3, §11.4).</summary>
public static class GridKeyMap
{
    public static GridKeyAction MapGridKey(Key key, ModifierKeys mods)
    {
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        bool ctrlAlt = (mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;
        if (key == Key.Home && mods == ModifierKeys.Control)
            return new(GridKeyCommand.Move, -CellAddress.MaxRows, -CellAddress.MaxColumns);
        if (ctrlAlt) return default;
        return key switch
        {
            Key.Up => new(GridKeyCommand.Move, -1, 0, shift),
            Key.Down => new(GridKeyCommand.Move, 1, 0, shift),
            Key.Left => new(GridKeyCommand.Move, 0, -1, shift),
            Key.Right => new(GridKeyCommand.Move, 0, 1, shift),
            Key.Home => new(GridKeyCommand.Move, 0, -CellAddress.MaxColumns, shift),
            Key.PageDown => new(GridKeyCommand.Page, 1, 0, shift),
            Key.PageUp => new(GridKeyCommand.Page, -1, 0, shift),
            Key.Enter => new(GridKeyCommand.Move, shift ? -1 : 1, 0),
            Key.Tab => new(GridKeyCommand.Move, 0, shift ? -1 : 1),
            Key.F2 when !shift => new(GridKeyCommand.EditExisting),
            Key.Delete when !shift => new(GridKeyCommand.Clear),
            _ => default,
        };
    }

    public static EditorKeyAction MapEditorKey(Key key, ModifierKeys mods)
    {
        if ((mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return default;
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        return key switch
        {
            Key.Enter => new(EditorKeyCommand.Commit, shift ? -1 : 1, 0),
            Key.Tab => new(EditorKeyCommand.Commit, 0, shift ? -1 : 1),
            Key.Escape => new(EditorKeyCommand.Cancel),
            _ => default,
        };
    }

    /// <summary>True when text input should start an edit: non-empty and free of control characters.</summary>
    public static bool IsPrintable(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char ch in text)
            if (char.IsControl(ch)) return false;
        return true;
    }
}
