using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Gridline.Core.Addressing;
using Microsoft.Win32;

namespace Gridline.App;

public partial class MainWindow : Window
{
    private const string FileFilter = "Gridline files (*.gridline)|*.gridline|All files (*.*)|*.*";
    private readonly DocumentSession _session;
    private readonly SheetController _controller;

    public MainWindow() : this(new DocumentSession()) { }

    public MainWindow(DocumentSession session)
    {
        _session = session;
        InitializeComponent();
        // The editor overlay follows the grid's layout constants.
        EditorLayer.Margin = new Thickness(GridControl.RowHeaderWidth, GridControl.HeaderHeight, 0, 0);
        CellEditor.Width = GridControl.ColumnWidth;
        CellEditor.Height = GridControl.RowHeight;
        _controller = new SheetController(Sheet.Selection) { Workbook = _session.Workbook };
        Sheet.Workbook = _session.Workbook;
        Title = _session.Title;
        _session.StateChanged += (_, _) => OnSessionStateChanged();

        Sheet.ViewportChanged += (_, _) => OnViewportChanged();
        Sheet.Selection.Changed += (_, _) => RefreshBars();
        Sheet.EditRequested += (_, initial) => BeginEdit(initial);
        Sheet.ClearRequested += (_, _) => _controller.ClearSelection();
        VScroll.ValueChanged += (_, e) => Sheet.TopRow = (int)Math.Round(e.NewValue);
        HScroll.ValueChanged += (_, e) => Sheet.LeftCol = (int)Math.Round(e.NewValue);
        Loaded += (_, _) => Sheet.Focus();
        RefreshBars();
    }

    private void OnSessionStateChanged()
    {
        Title = _session.Title;
        if (!ReferenceEquals(Sheet.Workbook, _session.Workbook))
        {
            _controller.Workbook = _session.Workbook; // cancels any in-cell edit
            HideEditor();
            Sheet.Workbook = _session.Workbook;
            Sheet.Selection.MoveTo(new CellAddress(1, 1), extend: false);
            Sheet.TopRow = 1;
            Sheet.LeftCol = 1;
        }
        RefreshBars();
    }

    private void OnViewportChanged()
    {
        SyncScrollBars();
        if (_controller.IsEditing) PositionEditor();
    }

    /// <summary>Scrollbar ranges follow §11.3: vertical 1..100000 → TopRow, horizontal 1..702 → LeftCol.</summary>
    private void SyncScrollBars()
    {
        VScroll.ViewportSize = Sheet.VisibleRowCount;
        VScroll.LargeChange = Sheet.VisibleRowCount;
        VScroll.Value = Sheet.TopRow;
        HScroll.ViewportSize = Sheet.VisibleColumnCount;
        HScroll.LargeChange = Sheet.VisibleColumnCount;
        HScroll.Value = Sheet.LeftCol;
    }

    /// <summary>Name box, formula bar and status bar follow the selection and workbook (§11.4, §11.7); a focused box is left alone.</summary>
    private void RefreshBars()
    {
        if (!NameBox.IsKeyboardFocusWithin) NameBox.Text = Sheet.Selection.Active.ToString();
        if (!FormulaBar.IsKeyboardFocusWithin)
            FormulaBar.Text = _controller.IsEditing ? CellEditor.Text : _controller.ActiveInput;
        SelectionValue.Text = _controller.ActiveValueText;
        SelectionSum.Text = _controller.SelectionSumText;
        MenuUndo.IsEnabled = _session.Workbook.CanUndo;
        MenuRedo.IsEnabled = _session.Workbook.CanRedo;
    }

    // ---- In-cell editor (§11.4) ----

    private void BeginEdit(string? initial)
    {
        if (_controller.IsEditing) return;
        Sheet.ScrollIntoView(Sheet.Selection.Active);
        string text = _controller.BeginEdit(initial);
        PositionEditor();
        CellEditor.Text = text;
        CellEditor.IsUndoEnabled = false; // start each edit with an empty text-undo stack
        CellEditor.IsUndoEnabled = true;
        CellEditor.Visibility = Visibility.Visible;
        CellEditor.Focus();
        CellEditor.CaretIndex = text.Length;
    }

    /// <summary>
    /// Ends the in-cell edit. <paramref name="refocusGrid"/> is true for Enter/Tab/Esc and commands; it is false when
    /// the edit ends because focus already moved elsewhere, so the element the user clicked keeps focus.
    /// </summary>
    private void EndEdit(bool commit, int dRow = 0, int dCol = 0, bool refocusGrid = true)
    {
        if (!_controller.IsEditing) return;
        if (commit)
        {
            try { _controller.CommitEdit(CellEditor.Text, dRow, dCol); }
            catch (ArgumentException ex)
            {
                // The controller keeps the edit open; nothing was applied.
                MessageBox.Show(this, ex.Message, "Gridline", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else _controller.CancelEdit();
        HideEditor(refocusGrid);
        RefreshBars();
    }

    private void HideEditor(bool refocusGrid = true)
    {
        if (refocusGrid && CellEditor.IsKeyboardFocusWithin) Sheet.Focus();
        CellEditor.Visibility = Visibility.Collapsed;
    }

    private void PositionEditor()
    {
        var r = Sheet.GetCellRect(_controller.EditAddress);
        Canvas.SetLeft(CellEditor, r.X - GridControl.RowHeaderWidth);
        Canvas.SetTop(CellEditor, r.Y - GridControl.HeaderHeight);
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        var action = GridKeyMap.MapEditorKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
        if (action.Command == EditorKeyCommand.None) return;
        EndEdit(action.Command == EditorKeyCommand.Commit, action.DRow, action.DCol);
        e.Handled = true;
    }

    /// <summary>Losing focus inside the window (e.g. clicking another cell) commits; window deactivation keeps the edit open.</summary>
    private void OnEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is not null) EndEdit(commit: true, refocusGrid: false);
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_controller.IsEditing && !FormulaBar.IsKeyboardFocusWithin) FormulaBar.Text = CellEditor.Text;
    }

    // ---- Name box and formula bar (§11.4) ----

    private void OnNameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _controller.TryNavigate(NameBox.Text); // the selection change scrolls into view
        else if (e.Key != Key.Escape) return;
        e.Handled = true;
        Sheet.Focus(); // the box reverts/refreshes on focus loss
    }

    private void OnNameBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        NameBox.Text = Sheet.Selection.Active.ToString();

    private void OnFormulaBarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _controller.CommitInput(FormulaBar.Text);
        else if (e.Key != Key.Escape) return;
        e.Handled = true;
        Sheet.Focus();
    }

    /// <summary>Uncommitted formula bar text is discarded when focus leaves without Enter.</summary>
    private void OnFormulaBarLostFocus(object sender, KeyboardFocusChangedEventArgs e) => RefreshBars();

    /// <summary>
    /// Opening a menu takes focus from the in-cell editor (which commits and collapses); WPF then tries to give focus
    /// back to the collapsed editor and fails. Once the menu has closed, return focus to the grid unless a visible
    /// text box (name box, formula bar) has it.
    /// </summary>
    private void OnSubmenuClosed(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (Keyboard.FocusedElement is not TextBox { IsVisible: true } && !Sheet.IsKeyboardFocused) Sheet.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);

    // ---- Commands ----

    private void OnNew(object s, ExecutedRoutedEventArgs e) => _session.New();

    private void OnOpen(object s, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = FileFilter };
        if (dlg.ShowDialog(this) != true) return;
        Report(_session.TryOpen(dlg.FileName, out var err), err);
    }

    private void OnSave(object s, ExecutedRoutedEventArgs e)
    {
        EndEdit(commit: true);
        if (_session.FilePath is null) OnSaveAs(s, e);
        else Report(_session.TrySave(out var err), err);
    }

    private void OnSaveAs(object s, ExecutedRoutedEventArgs e)
    {
        EndEdit(commit: true);
        var dlg = new SaveFileDialog { Filter = FileFilter, DefaultExt = ".gridline" };
        if (dlg.ShowDialog(this) != true) return;
        Report(_session.TrySaveAs(dlg.FileName, out var err), err);
    }

    // Ctrl+Z/Ctrl+Y reach the window's Undo/Redo bindings only when the grid (not a TextBox) has focus: a focused
    // TextBox handles them as text undo (DECISIONS.md). The menu items call the workbook directly, so their action and
    // enabled state never depend on which element has focus.
    private void OnUndo(object s, ExecutedRoutedEventArgs e) => UndoRedo(undo: true);
    private void OnRedo(object s, ExecutedRoutedEventArgs e) => UndoRedo(undo: false);
    private void OnUndoClick(object s, RoutedEventArgs e) => UndoRedo(undo: true);
    private void OnRedoClick(object s, RoutedEventArgs e) => UndoRedo(undo: false);

    private void UndoRedo(bool undo)
    {
        EndEdit(commit: false);
        if (undo) _session.Workbook.Undo();
        else _session.Workbook.Redo();
    }

    private void CanUndo(object s, CanExecuteRoutedEventArgs e) => e.CanExecute = _session.Workbook.CanUndo;
    private void CanRedo(object s, CanExecuteRoutedEventArgs e) => e.CanExecute = _session.Workbook.CanRedo;
    private void OnExit(object s, ExecutedRoutedEventArgs e) => Close();

    private void Report(bool ok, string? error)
    {
        if (!ok) MessageBox.Show(this, error, "Gridline", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
