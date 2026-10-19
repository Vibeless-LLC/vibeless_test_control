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
