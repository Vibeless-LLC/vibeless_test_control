using System.Globalization;

namespace Gridline.App;

/// <summary>UI-free rules behind the sheet tab strip and its context menu (CR1.8).</summary>
public static class SheetTabs
{
    /// <summary>The smallest <c>SheetN</c> (N ≥ 1) not in <paramref name="existing"/>, compared case-insensitively.</summary>
    public static string NextSheetName(IReadOnlyList<string> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        for (int n = 1; ; n++)
        {
            string name = "Sheet" + n.ToString(CultureInfo.InvariantCulture);
            if (!taken.Contains(name)) return name;
        }
    }

    /// <summary>Delete is offered only when another sheet remains.</summary>
    public static bool CanDelete(int count) => count > 1;

    public static bool CanMoveLeft(int index) => index > 0;

    public static bool CanMoveRight(int index, int count) => index < count - 1;

    /// <summary>
    /// Runs a sheet operation; an <see cref="ArgumentException"/> (invalid, duplicate or unknown name, index out of
    /// range) or <see cref="InvalidOperationException"/> (last sheet, 255 sheets, over-length rewrite) is reported as
    /// <paramref name="error"/>. Core changes nothing when it throws these.
    /// </summary>
    public static bool TryRun(Action action, out string? error)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }
}
