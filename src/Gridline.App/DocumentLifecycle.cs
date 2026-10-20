namespace Gridline.App;

/// <summary>The user's answer to the unsaved-changes prompt (§11.5).</summary>
public enum UnsavedChoice { Save, DontSave, Cancel }

/// <summary>Asks the user what to do with unsaved changes; the real implementation is a modal dialog.</summary>
public interface IUnsavedChangesPrompt
{
    UnsavedChoice Ask(string documentName);
}

/// <summary>
/// UI-free New / Open / Save / Save As / Exit flows (§11.5). Dialogs are injected: <c>pickSavePath</c> and
/// <c>pickOpenPath</c> return null when the user cancels; <c>reportError</c> shows an error.
/// </summary>
public sealed class DocumentLifecycle(
    DocumentSession session,
    IUnsavedChangesPrompt prompt,
    Func<string?> pickSavePath,
    Func<string?> pickOpenPath,
    Action<string> reportError,
    Func<string?>? pickCsvImportPath = null,
    Func<string?>? pickCsvExportPath = null)
{
    /// <summary>True when it is fine to discard the current document: clean, Don't Save, or a completed Save.</summary>
    public bool ConfirmDiscard()
    {
        if (!session.IsDirty) return true;
        return prompt.Ask(session.DocumentName) switch
        {
            UnsavedChoice.DontSave => true,
            UnsavedChoice.Save => Save(),
            _ => false,
        };
    }

    /// <summary>Saves to the current path, or runs Save As when untitled. False when cancelled or failed.</summary>
    public bool Save()
    {
        if (session.FilePath is null) return SaveAs();
        return Report(session.TrySave(out var err), err);
    }

    public bool SaveAs()
    {
        string? path = pickSavePath();
        if (path is null) return false;
        return Report(session.TrySaveAs(path, out var err), err);
    }

    /// <summary>New document if the current one may be discarded.</summary>
    public bool New()
    {
        if (!ConfirmDiscard()) return false;
        session.New();
        return true;
    }

    /// <summary>Open a file if the current document may be discarded; a load failure is reported and the document kept.</summary>
    public bool Open()
    {
        if (!ConfirmDiscard()) return false;
        string? path = pickOpenPath();
        if (path is null) return false;
        return Report(session.TryOpen(path, out var err), err);
    }

    /// <summary>Import a CSV file as an untitled dirty document if the current one may be discarded (§11.5).</summary>
    public bool ImportCsv()
    {
        if (pickCsvImportPath is null || !ConfirmDiscard()) return false;
        string? path = pickCsvImportPath();
        if (path is null) return false;
        return Report(session.TryImportCsv(path, out var err), err);
    }

    /// <summary>Exports the workbook as CSV; no prompt, the document state is unchanged.</summary>
    public bool ExportCsv()
    {
        if (pickCsvExportPath is null) return false;
        string? path = pickCsvExportPath();
        if (path is null) return false;
        return Report(session.TryExportCsv(path, out var err), err);
    }

    /// <summary>True when the application may exit.</summary>
    public bool Exit() => ConfirmDiscard();

    private bool Report(bool ok, string? error)
    {
        if (!ok) reportError(error ?? "Unknown error.");
        return ok;
    }
}
