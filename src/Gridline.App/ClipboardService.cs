using System.Runtime.InteropServices;
using System.Text;
using Gridline.Core;
using Gridline.Core.Addressing;
using Gridline.IO;

namespace Gridline.App;

/// <summary>Text access to the system clipboard; the WPF adapter is the only implementation that touches WPF.</summary>
public interface IClipboard
{
    /// <summary>The clipboard text, or null when it holds none. May throw <see cref="ExternalException"/> while another process holds the clipboard.</summary>
    string? GetText();

    /// <summary>Replaces the clipboard text. May throw <see cref="ExternalException"/> while another process holds the clipboard.</summary>
    void SetText(string text);
}

/// <summary>Outcome of a clipboard command; <see cref="Message"/> is a status-bar text (empty on plain success).</summary>
public readonly record struct ClipboardResult(bool Success, string Message)
{
    public static ClipboardResult Ok { get; } = new(true, "");
    public static ClipboardResult Fail(string message) => new(false, message);
}

/// <summary>
/// UI-free copy/paste (§11.6). Copy remembers the source range and puts the displayed values on the system clipboard
/// as TSV. Paste of text identical to what this app last copied (same workbook) is a §9.4 <see cref="Workbook.CopyPaste"/>;
/// any other text is parsed with the §8.5 import rules (Tab delimiter) and written at the active cell as one ApplyBatch.
/// </summary>
public sealed class ClipboardService
{
    /// <summary>Largest clipped copy area (positions) that is rendered to system clipboard text.</summary>
    public const int MaxCopyPositions = 4_000_000;

    private const int Attempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(40);

    private readonly IClipboard _clipboard;
    private readonly Func<Workbook?> _workbook;
    private readonly Action<TimeSpan> _sleep;

    private Workbook? _copiedFrom;
    private RangeAddress _source;
    private string? _lastText;

    public ClipboardService(IClipboard clipboard, Func<Workbook?> workbook, Action<TimeSpan>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(workbook);
        _clipboard = clipboard;
        _workbook = workbook;
        _sleep = sleep ?? Thread.Sleep;
    }

    public ClipboardResult Copy(RangeAddress range)
    {
        if (_workbook() is not { } wb) return ClipboardResult.Fail("No document.");
        string text = BuildText(wb, range, out bool tooLarge);

        if (!Try(() => _clipboard.SetText(text), out _))
            return ClipboardResult.Fail("The clipboard is busy; copy failed.");
        _copiedFrom = wb;
        _source = range;
        _lastText = text;
        return tooLarge
            ? new ClipboardResult(true, "Selection too large for the system clipboard; it can be pasted within Gridline only.")
            : ClipboardResult.Ok;
    }

    public ClipboardResult Paste(CellAddress active)
    {
        if (_workbook() is not { } wb) return ClipboardResult.Fail("No document.");
        string? text = null;
        if (!Try(() => text = _clipboard.GetText(), out _))
            return ClipboardResult.Fail("The clipboard is busy; paste failed.");

        try
        {
            if (_lastText is not null && _copiedFrom == wb && string.Equals(text ?? (_lastText.Length == 0 ? "" : null), _lastText, StringComparison.Ordinal))
            {
                wb.CopyPaste(_source.ToString(), active.ToString());
                return ClipboardResult.Ok;
            }
            if (string.IsNullOrEmpty(text)) return ClipboardResult.Fail("Nothing to paste.");
            return PasteExternal(wb, text, active);
        }
        catch (ArgumentException ex) { return ClipboardResult.Fail("Cannot paste: " + ex.Message); }
    }

    private static ClipboardResult PasteExternal(Workbook wb, string text, CellAddress active)
    {
        IReadOnlyList<IReadOnlyList<string>> records;
        try { records = DelimitedReader.Parse(text, '\t'); }
        catch (GridlineFormatException ex) { return ClipboardResult.Fail("Cannot paste: " + ex.Message); }

        var edits = new List<KeyValuePair<string, string>>();
        for (int i = 0; i < records.Count; i++)
        {
            long row = (long)active.Row + i;
            if (row > Workbook.MaxRows) break;
            var fields = records[i];
            for (int j = 0; j < fields.Count; j++)
            {
                long col = (long)active.Col + j;
                if (col > Workbook.MaxColumns) break; // fields past the grid edge are dropped
                edits.Add(new(new CellAddress((int)row, (int)col).ToString(), DelimitedMapping.ToInput(fields[j])));
            }
        }
        if (edits.Count > 0) wb.ApplyBatch(edits);
        return ClipboardResult.Ok;
    }

    /// <summary>
    /// Displayed values (<see cref="CellFormatter"/>) joined by Tab, CRLF between rows, clipped to the intersection of
    /// the range with the used cells' bounding extent (rows/columns beyond the last used one in the range are
    /// omitted); "" when the range holds no used cell or the clipped area exceeds <see cref="MaxCopyPositions"/>
    /// (<paramref name="tooLarge"/>: the internal source range still works for in-app paste).
    /// </summary>
    private static string BuildText(Workbook wb, RangeAddress range, out bool tooLarge)
    {
        tooLarge = false;
        int maxRow = 0, maxCol = 0;
        foreach (string a in wb.GetUsedAddresses())
        {
            var c = CellAddress.Parse(a);
            if (!range.Contains(c)) continue;
            if (c.Row > maxRow) maxRow = c.Row;
            if (c.Col > maxCol) maxCol = c.Col;
        }
        if (maxRow == 0) return "";
        int rows = maxRow - range.TopLeft.Row + 1, cols = maxCol - range.TopLeft.Col + 1;
        if ((long)rows * cols > MaxCopyPositions)
        {
            tooLarge = true;
            return "";
        }

        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            if (r > 0) sb.Append("\r\n");
            for (int c = 0; c < cols; c++)
            {
                if (c > 0) sb.Append('\t');
                string addr = new CellAddress(range.TopLeft.Row + r, range.TopLeft.Col + c).ToString();
                sb.Append(CellFormatter.Format(wb.GetValue(addr)).Text);
            }
        }
        return sb.ToString();
    }

    /// <summary>Runs <paramref name="action"/>, retrying briefly while the clipboard is held by another process.</summary>
    private bool Try(Action action, out Exception? error)
    {
        error = null;
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try { action(); return true; }
            catch (ExternalException ex) { error = ex; } // includes COMException (CLIPBRD_E_CANT_OPEN)
            if (attempt < Attempts) _sleep(RetryDelay);
        }
        return false;
    }
}

/// <summary>WPF-backed <see cref="IClipboard"/>; must be used on the UI thread.</summary>
public sealed class WpfClipboard : IClipboard
{
    public string? GetText() => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;

    public void SetText(string text) => System.Windows.Clipboard.SetText(text);
}
