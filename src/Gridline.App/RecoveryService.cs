using System.IO;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.App;

/// <summary>Autosave and crash-recovery files for one session (§11.6): <c>&lt;dir&gt;\&lt;session-guid&gt;.gridline</c>.</summary>
public sealed class RecoveryService(string recoveryDir, Guid sessionId)
{
    public const string EnvVar = "GRIDLINE_RECOVERY_DIR";

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gridline", "Recovery");

    /// <summary>The recovery directory: the <c>GRIDLINE_RECOVERY_DIR</c> override when set, else <see cref="DefaultDir"/>.</summary>
    public static string ResolveDir()
    {
        string? env = Environment.GetEnvironmentVariable(EnvVar);
        return string.IsNullOrWhiteSpace(env) ? DefaultDir : env;
    }

    public string RecoveryPath { get; } = Path.Combine(recoveryDir, sessionId.ToString("D") + ".gridline");

    /// <summary>Saves atomically when the workbook is dirty; does nothing otherwise.</summary>
    public void AutosaveIfDirty(Workbook wb) => AutosaveIfDirty(wb, wb.IsDirty);

    /// <summary>Saves when the session is dirty (including a restored, not yet saved document).</summary>
    public void AutosaveIfDirty(DocumentSession session) => AutosaveIfDirty(session.Workbook, session.IsDirty);

    private void AutosaveIfDirty(Workbook wb, bool dirty)
    {
        if (!dirty) return;
        Directory.CreateDirectory(recoveryDir);
        GridlineFile.SaveToPath(wb, RecoveryPath);
    }

    /// <summary>Removes this session's recovery file; a missing file is fine.</summary>
    public void Delete() => DeleteFile(RecoveryPath);

    /// <summary>The <c>*.gridline</c> files in the directory other than this session's own, oldest first.</summary>
    public IReadOnlyList<string> FindRecoveryFiles()
    {
        if (!Directory.Exists(recoveryDir)) return [];
        string own = Path.GetFullPath(RecoveryPath);
        return Directory.GetFiles(recoveryDir, "*.gridline")
            .Where(f => !string.Equals(Path.GetFullPath(f), own, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => File.GetLastWriteTimeUtc(f)).ThenBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Deletes a recovery file, ignoring one that is already gone or locked.</summary>
    public static void DeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
