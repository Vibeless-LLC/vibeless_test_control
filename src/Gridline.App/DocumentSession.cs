using System.IO;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.App;

/// <summary>UI-free document state: the workbook, its file path and the window title (§11.1).</summary>
public sealed class DocumentSession
{
    private Workbook _workbook;
    private string? _filePath;

    public DocumentSession()
    {
        _workbook = new Workbook();
        _workbook.CellsChanged += OnCellsChanged;
    }

    public Guid SessionId { get; } = Guid.NewGuid();

    public Workbook Workbook => _workbook;

    public string? FilePath => _filePath;

    public string Title =>
        "Gridline - " + (_filePath is null ? "Untitled" : Path.GetFileName(_filePath)) +
        (_workbook.IsDirty ? " *" : "");

    /// <summary>Raised on any dirty, title or workbook change.</summary>
    public event EventHandler? StateChanged;

    public void New() => Replace(new Workbook(), null);

    public void Open(string path) => Replace(GridlineFile.LoadFromPath(path), path);

    public void Save()
    {
        if (_filePath is null) throw new InvalidOperationException("Save requires a file path; use SaveAs.");
        SaveTo(_filePath);
    }

    public void SaveAs(string path) => SaveTo(path);

    /// <summary>Like <see cref="Open"/> but reports expected failures as a message; the session is unchanged on failure.</summary>
    public bool TryOpen(string path, out string? error) => Try(() => Open(path), out error);

    public bool TrySave(out string? error) => Try(Save, out error);

    public bool TrySaveAs(string path, out string? error) => Try(() => SaveAs(path), out error);

    private static bool Try(Action action, out string? error)
    {
        try { action(); error = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or GridlineFormatException
                                       or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    private void SaveTo(string path)
    {
        GridlineFile.SaveToPath(_workbook, path);
        _workbook.MarkSaved();
        _filePath = path;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Replace(Workbook wb, string? path)
    {
        _workbook.CellsChanged -= OnCellsChanged;
        _workbook = wb;
        _workbook.CellsChanged += OnCellsChanged;
        _filePath = path;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCellsChanged(object? sender, CellsChangedEventArgs e) =>
        StateChanged?.Invoke(this, EventArgs.Empty);
}
