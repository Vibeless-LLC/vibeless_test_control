using System.IO;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.App;

/// <summary>Autosave and crash-recovery files for one session (§11.6): <c>&lt;dir&gt;\&lt;session-guid&gt;.gridline</c>.</summary>
public sealed class RecoveryService(string recoveryDir, Guid sessionId) : IDisposable
{
    private FileStream? _lock;
    private bool _lockTried;

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

    /// <summary>Path of this session's lock file; a live instance holds it open with FileShare.None.</summary>
    public string LockPath { get; } = Path.Combine(recoveryDir, sessionId.ToString("D") + ".lock");

    /// <summary>
    /// Creates and holds this session's lock file (deleted when the handle closes, even after a crash) so other
    /// instances do not offer this session's recovery file. Failure is tolerated: autosave still works.
    /// </summary>
    public void AcquireLock()
    {
        if (_lockTried) return;
        _lockTried = true;
        try
        {
            Directory.CreateDirectory(recoveryDir);
            _lock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception) { _lock = null; }
    }

    /// <summary>Releases (and deletes) the lock file. The recovery file itself is left; use <see cref="Delete"/>.</summary>
    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
    }

    /// <summary>Saves atomically when the workbook is dirty; does nothing otherwise.</summary>
    public void AutosaveIfDirty(Workbook wb) => AutosaveIfDirty(wb, wb.IsDirty);

    /// <summary>Saves when the session is dirty (including a restored, not yet saved document).</summary>
    public void AutosaveIfDirty(DocumentSession session) => AutosaveIfDirty(session.Workbook, session.IsDirty);

    private void AutosaveIfDirty(Workbook wb, bool dirty)
    {
        if (!dirty) return;
        AcquireLock();
        Directory.CreateDirectory(recoveryDir);
        GridlineFile.SaveToPath(wb, RecoveryPath);
    }

    /// <summary>Removes this session's recovery file; a missing file is fine.</summary>
    public void Delete() => DeleteFile(RecoveryPath);

    /// <summary>
    /// The <c>*.gridline</c> files in the directory that belong to no live instance, oldest first. A file whose
    /// <c>.lock</c> cannot be opened exclusively is held by a running instance and is skipped; openable (stale) locks
    /// are removed. Never throws: an unusable directory yields an empty list.
    /// </summary>
    public IReadOnlyList<string> FindRecoveryFiles()
    {
        try
        {
            AcquireLock();
            if (!Directory.Exists(recoveryDir)) return [];
            string own = Path.GetFullPath(RecoveryPath);
            var result = new List<string>();
            foreach (var f in Directory.GetFiles(recoveryDir, "*.gridline"))
            {
                if (string.Equals(Path.GetFullPath(f), own, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsHeldByLiveInstance(Path.ChangeExtension(f, ".lock"))) continue;
                result.Add(f);
            }
            foreach (var l in Directory.GetFiles(recoveryDir, "*.lock")) // stale locks without a recovery file
                if (!string.Equals(Path.GetFullPath(l), Path.GetFullPath(LockPath), StringComparison.OrdinalIgnoreCase))
                    IsHeldByLiveInstance(l);
            return result.OrderBy(f => File.GetLastWriteTimeUtc(f)).ThenBy(f => f, StringComparer.Ordinal).ToList();
        }
        catch (Exception) { return []; }
    }

    /// <summary>True when the lock file exists and another process holds it. A stale lock is deleted.</summary>
    private static bool IsHeldByLiveInstance(string lockPath)
    {
        if (!File.Exists(lockPath)) return false;
        try
        {
            using (new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>Deletes a recovery file, ignoring one that is already gone or locked.</summary>
    public static void DeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception) { }
    }
}
