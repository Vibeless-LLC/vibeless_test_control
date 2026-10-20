using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Gridline.Core.Addressing;
using Microsoft.Win32;

namespace Gridline.App;

public partial class MainWindow : Window
{
    private const string FileFilter = "Gridline (*.gridline)|*.gridline";
    private readonly DocumentSession _session;
    private readonly SheetController _controller;
    private readonly DocumentLifecycle _lifecycle;

    private readonly RecoveryService? _recovery;
    private bool _autosaveFailed;
    private readonly System.Windows.Threading.DispatcherTimer? _autosaveTimer;

    public MainWindow() : this(new DocumentSession()) { }

    /// <param name="recovery">Autosave/recovery service, or null to disable autosave.</param>
    /// <param name="autosaveInterval">Autosave period; 60 s when null (§11.6). Injectable for tests.</param>
    public MainWindow(DocumentSession session, RecoveryService? recovery = null, TimeSpan? autosaveInterval = null)
    {
        _session = session;
        _recovery = recovery;
        InitializeComponent();
        _lifecycle = new DocumentLifecycle(_session, new WindowPrompt(this), PickSavePath, PickOpenPath, ShowError);
        Closing += OnWindowClosing;
        // The editor overlay follows the grid's layout constants.
        EditorLayer.Margin = new Thickness(GridControl.RowHeaderWidth, GridControl.HeaderHeight, 0, 0);
        CellEditor.Width = GridControl.ColumnWidth;
        CellEditor.Height = GridControl.RowHeight;
        _controller = new SheetController(Sheet.Selection) { Workbook = _session.Workbook };
        Sheet.Workbook = _session.Workbook;
        Title = _session.Title;
        _session.StateChanged += (_, _) => OnSessionStateChanged();
        if (_recovery is not null)
        {
            _session.Saved += (_, _) => _recovery.Delete();
            _autosaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = autosaveInterval ?? TimeSpan.FromSeconds(60) };
            _autosaveTimer.Tick += (_, _) => Autosave();
            _autosaveTimer.Start();
            Closed += (_, _) => { _autosaveTimer.Stop(); _recovery.Delete(); _recovery.Dispose(); }; // Closed only follows a non-cancelled close
        }

        Sheet.ViewportChanged += (_, _) => OnViewportChanged();
        Sheet.Selection.Changed += (_, _) => RefreshBars();
        Sheet.EditRequested += (_, initial) => BeginEdit(initial);
        Sheet.ClearRequested += (_, _) => _controller.ClearSelection();
        VScroll.ValueChanged += (_, e) => Sheet.TopRow = (int)Math.Round(e.NewValue);
        HScroll.ValueChanged += (_, e) => Sheet.LeftCol = (int)Math.Round(e.NewValue);
        Loaded += (_, _) => Sheet.Focus();
        RefreshBars();
    }

    private void Autosave()
    {
        try
        {
            _recovery?.AutosaveIfDirty(_session);
            if (_autosaveFailed) { _autosaveFailed = false; AutosaveStatus.Text = ""; }
        }
        catch (Exception)
        {
            // Never throw out of the timer tick; tell the user once in the status bar and retry on the next tick.
            if (!_autosaveFailed) { _autosaveFailed = true; AutosaveStatus.Text = "Autosave failed"; }
        }
    }

    /// <summary>Offers each recovery file left by an earlier session (§11.6); call once the window is shown.</summary>
    public void OfferRecovery()
    {
        if (_recovery is null) return;
        try
        {
            var files = _recovery.FindRecoveryFiles();
            if (files.Count == 0) return;
            var dlg = new RecoveryDialog(files, Restore, RecoveryService.DeleteFile) { Owner = this };
            dlg.ShowDialog();
            Sheet.Focus();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AutosaveStatus.Text = "Recovery unavailable";
        }
    }

    private string? Restore(string path)
    {
        EndEdit(commit: true);
        if (!_lifecycle.ConfirmDiscard()) return "Restore cancelled.";
        if (!_session.TryOpenRecovered(path, out var error)) return error;
        RecoveryService.DeleteFile(path);
        Autosave(); // this session's own recovery file replaces the one just deleted
        return null;
    }

    private void OnSessionStateChanged()
    {
        Title = _session.Title;
        if (!_session.IsDirty) _recovery?.Delete(); // undone/new/opened back to clean: nothing left to recover
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

    private void OnNew(object s, ExecutedRoutedEventArgs e) { if (CommitPending()) _lifecycle.New(); }

    private void OnOpen(object s, ExecutedRoutedEventArgs e) { if (CommitPending()) _lifecycle.Open(); }

    private void OnSave(object s, ExecutedRoutedEventArgs e) { if (CommitPending()) _lifecycle.Save(); }

    private void OnSaveAs(object s, ExecutedRoutedEventArgs e) { if (CommitPending()) _lifecycle.SaveAs(); }

    /// <summary>Commits an open in-cell edit; false (and the edit stays open) if the commit failed.</summary>
    private bool CommitPending()
    {
        EndEdit(commit: true);
        return !_controller.IsEditing;
    }

    private string? PickSavePath()
    {
        var dlg = new SaveFileDialog { Filter = FileFilter, DefaultExt = ".gridline" };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private string? PickOpenPath()
    {
        var dlg = new OpenFileDialog { Filter = FileFilter };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Gridline", MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>The window close button and File > Exit both land here (§11.5).</summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!CommitPending() || !_lifecycle.Exit()) e.Cancel = true;
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
}

/// <summary>Creates a fresh modal dialog per prompt.</summary>
internal sealed class WindowPrompt(Window owner) : IUnsavedChangesPrompt
{
    public UnsavedChoice Ask(string documentName) => new UnsavedChangesDialog(owner).Ask(documentName);
}
