using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using Gridline.Core;
using Gridline.Core.Addressing;

namespace Gridline.App;

/// <summary>
/// Virtualized sheet view (§11.3): draws only the headers and cells inside the viewport, so the work per frame is
/// O(visible cells) wherever <see cref="TopRow"/>/<see cref="LeftCol"/> are.
/// </summary>
public sealed class GridControl : FrameworkElement
{
    public const double RowHeight = 22;
    public const double ColumnWidth = 80;
    public const double HeaderHeight = 22;
    public const double RowHeaderWidth = 56;
    private const double CellPadding = 3;
    private const int MaxDisplayChars = 256;
    private const double FontSize = 12;

    private static readonly Brush CellBackground = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush HeaderBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)));
    private static readonly Brush HeaderSelectedBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xD3, 0xE3, 0xF5)));
    private static readonly Brush SelectionFill = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x33, 0x77, 0xCC)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Colors.Black));
    private static readonly Pen GridLinePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)), 1));
    private static readonly Pen HeaderLinePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)), 1));
    private static readonly Pen ActivePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x21, 0x73, 0x46)), 2));
    private static readonly Typeface CellTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private Workbook? _workbook;
    private int _topRow = 1;
    private int _leftCol = 1;
    private bool _dragging;

    public GridControl()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        Selection.Changed += (_, _) =>
        {
            ScrollIntoView(Selection.Active);
            InvalidateVisual();
        };
    }

    /// <summary>The workbook shown; the control re-renders on its <see cref="Workbook.CellsChanged"/>.</summary>
    public Workbook? Workbook
    {
        get => _workbook;
        set
        {
            if (ReferenceEquals(_workbook, value)) return;
            if (_workbook is not null) _workbook.CellsChanged -= OnCellsChanged;
            _workbook = value;
            if (_workbook is not null) _workbook.CellsChanged += OnCellsChanged;
            InvalidateVisual();
        }
    }

    public GridSelection Selection { get; } = new();

    /// <summary>First visible row, 1..100000.</summary>
    public int TopRow
    {
        get => _topRow;
        set => SetViewport(value, _leftCol);
    }

    /// <summary>First visible column, 1..702.</summary>
    public int LeftCol
    {
        get => _leftCol;
        set => SetViewport(_topRow, value);
    }

    /// <summary>Rows that fit entirely in the viewport (at least 1).</summary>
    public int VisibleRowCount => Math.Max(1, (int)Math.Floor((ActualHeight - HeaderHeight) / RowHeight));

    /// <summary>Columns that fit entirely in the viewport (at least 1).</summary>
    public int VisibleColumnCount => Math.Max(1, (int)Math.Floor((ActualWidth - RowHeaderWidth) / ColumnWidth));

    /// <summary>Asks the host to start an in-cell edit; the argument is the initial text, or null for the existing input (F2).</summary>
    public event EventHandler<string?>? EditRequested;

    /// <summary>Asks the host to clear the selection (Delete).</summary>
    public event EventHandler? ClearRequested;

    /// <summary>Raised when <see cref="TopRow"/>, <see cref="LeftCol"/> or the viewport size changes.</summary>
    public event EventHandler? ViewportChanged;

    /// <summary>Scrolls the minimum amount that makes <paramref name="a"/> fully visible.</summary>
    public void ScrollIntoView(CellAddress a)
    {
        int top = _topRow, left = _leftCol;
        if (a.Row < top) top = a.Row;
        else if (a.Row >= top + VisibleRowCount) top = a.Row - VisibleRowCount + 1;
        if (a.Col < left) left = a.Col;
        else if (a.Col >= left + VisibleColumnCount) left = a.Col - VisibleColumnCount + 1;
        SetViewport(top, left);
    }

    /// <summary>The cell's rectangle in control coordinates (may lie outside the viewport).</summary>
    public Rect GetCellRect(CellAddress a) => new(
        RowHeaderWidth + (a.Col - _leftCol) * ColumnWidth,
        HeaderHeight + (a.Row - _topRow) * RowHeight,
        ColumnWidth, RowHeight);

    /// <summary>The cell under a point, clamped to the grid; points over the headers map to the first visible row/column.</summary>
    public CellAddress HitTestCell(Point p)
    {
        long row = _topRow + (long)Math.Floor((Math.Max(p.Y, HeaderHeight) - HeaderHeight) / RowHeight);
        long col = _leftCol + (long)Math.Floor((Math.Max(p.X, RowHeaderWidth) - RowHeaderWidth) / ColumnWidth);
        return new CellAddress((int)Math.Clamp(row, 1, CellAddress.MaxRows), (int)Math.Clamp(col, 1, CellAddress.MaxColumns));
    }

    private void SetViewport(int top, int left)
    {
        top = Math.Clamp(top, 1, CellAddress.MaxRows);
        left = Math.Clamp(left, 1, CellAddress.MaxColumns);
        if (top == _topRow && left == _leftCol) return;
        _topRow = top;
        _leftCol = left;
        InvalidateVisual();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCellsChanged(object? sender, CellsChangedEventArgs e) => InvalidateVisual();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Partially visible trailing row/column included; never beyond the grid.
        int rows = Math.Min((int)Math.Ceiling(Math.Max(0, height - HeaderHeight) / RowHeight), CellAddress.MaxRows - _topRow + 1);
        int cols = Math.Min((int)Math.Ceiling(Math.Max(0, width - RowHeaderWidth) / ColumnWidth), CellAddress.MaxColumns - _leftCol + 1);
        double cellsRight = RowHeaderWidth + cols * ColumnWidth, cellsBottom = HeaderHeight + rows * RowHeight;

        dc.DrawRectangle(CellBackground, null, new Rect(0, 0, width, height));

        var range = Selection.Range;
        var selRect = Rect.Intersect(SpanRect(range), new Rect(RowHeaderWidth, HeaderHeight, width, height));
        if (!selRect.IsEmpty) dc.DrawRectangle(SelectionFill, null, selRect);

        // Column letters once per frame; cell addresses are built from them.
        var letters = new string[cols];
        for (int c = 0; c < cols; c++) letters[c] = ColumnName.ToLetters(_leftCol + c);

        if (_workbook is { } wb)
        {
            for (int r = 0; r < rows; r++)
            {
                string rowText = (_topRow + r).ToString(CultureInfo.InvariantCulture);
                double y = HeaderHeight + r * RowHeight;
                for (int c = 0; c < cols; c++)
                {
                    var (text, align) = CellFormatter.Format(wb.GetValue(letters[c] + rowText));
                    if (text.Length == 0) continue;
                    DrawText(dc, text, align, new Rect(RowHeaderWidth + c * ColumnWidth, y, ColumnWidth, RowHeight), pixelsPerDip);
                }
            }
        }

        // Grid lines as one geometry.
        var lines = new StreamGeometry();
        using (var g = lines.Open())
        {
            for (int r = 1; r <= rows; r++)
            {
                double y = HeaderHeight + r * RowHeight - 0.5;
                g.BeginFigure(new Point(RowHeaderWidth, y), false, false);
                g.LineTo(new Point(cellsRight, y), true, false);
            }
            for (int c = 1; c <= cols; c++)
            {
                double x = RowHeaderWidth + c * ColumnWidth - 0.5;
                g.BeginFigure(new Point(x, HeaderHeight), false, false);
                g.LineTo(new Point(x, cellsBottom), true, false);
            }
        }
        lines.Freeze();
        dc.DrawGeometry(null, GridLinePen, lines);

        // Headers (drawn over the cells so they stay put).
        dc.DrawRectangle(HeaderBackground, null, new Rect(0, 0, width, HeaderHeight));
        dc.DrawRectangle(HeaderBackground, null, new Rect(0, 0, RowHeaderWidth, height));
        var header = new StreamGeometry();
        using (var g = header.Open())
        {
            for (int c = 0; c < cols; c++)
            {
                var rect = new Rect(RowHeaderWidth + c * ColumnWidth, 0, ColumnWidth, HeaderHeight);
                int col = _leftCol + c;
                if (col >= range.TopLeft.Col && col <= range.BottomRight.Col)
                    dc.DrawRectangle(HeaderSelectedBackground, null, rect);
                DrawText(dc, letters[c], TextAlignment.Center, rect, pixelsPerDip);
                g.BeginFigure(new Point(rect.Right - 0.5, 0), false, false);
                g.LineTo(new Point(rect.Right - 0.5, HeaderHeight), true, false);
            }
            for (int r = 0; r < rows; r++)
            {
                var rect = new Rect(0, HeaderHeight + r * RowHeight, RowHeaderWidth, RowHeight);
                int row = _topRow + r;
                if (row >= range.TopLeft.Row && row <= range.BottomRight.Row)
                    dc.DrawRectangle(HeaderSelectedBackground, null, rect);
                DrawText(dc, row.ToString(CultureInfo.InvariantCulture), TextAlignment.Center, rect, pixelsPerDip);
                g.BeginFigure(new Point(0, rect.Bottom - 0.5), false, false);
                g.LineTo(new Point(RowHeaderWidth, rect.Bottom - 0.5), true, false);
            }
            g.BeginFigure(new Point(0, HeaderHeight - 0.5), false, false);
            g.LineTo(new Point(width, HeaderHeight - 0.5), true, false);
            g.BeginFigure(new Point(RowHeaderWidth - 0.5, 0), false, false);
            g.LineTo(new Point(RowHeaderWidth - 0.5, height), true, false);
        }
        header.Freeze();
        dc.DrawGeometry(null, HeaderLinePen, header);

        // Active cell outline, clipped to the cell area.
        var active = Selection.Active;
        if (active.Row >= _topRow && active.Row < _topRow + rows && active.Col >= _leftCol && active.Col < _leftCol + cols)
        {
            dc.PushClip(new RectangleGeometry(new Rect(RowHeaderWidth, HeaderHeight, width, height)));
            var r = GetCellRect(active);
            r.Inflate(-1, -1);
            dc.DrawRectangle(null, ActivePen, r);
            dc.Pop();
        }
    }

    /// <summary>The range's rectangle in control coordinates, computed without overflow for huge ranges.</summary>
    private Rect SpanRect(RangeAddress r)
    {
        double x1 = RowHeaderWidth + (double)(r.TopLeft.Col - _leftCol) * ColumnWidth;
        double y1 = HeaderHeight + (double)(r.TopLeft.Row - _topRow) * RowHeight;
        return new Rect(x1, y1, r.Cols * ColumnWidth, r.Rows * RowHeight);
    }

    private static void DrawText(DrawingContext dc, string text, TextAlignment align, Rect cell, double pixelsPerDip)
    {
        if (text.Length > MaxDisplayChars) text = text[..MaxDisplayChars];
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, CellTypeface,
            FontSize, TextBrush, pixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, cell.Width - 2 * CellPadding),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = align,
        };
        dc.DrawText(ft, new Point(cell.X + CellPadding, cell.Y + (cell.Height - ft.Height) / 2));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var action = GridKeyMap.MapGridKey(key, Keyboard.Modifiers);
        switch (action.Command)
        {
            case GridKeyCommand.Move:
                Selection.MoveBy(action.DRow, action.DCol, action.Extend);
                break;
            case GridKeyCommand.Page:
                TopRow += action.DRow * VisibleRowCount;
                Selection.MoveBy(action.DRow * VisibleRowCount, 0, action.Extend);
                break;
            case GridKeyCommand.EditExisting:
                EditRequested?.Invoke(this, null);
                break;
            case GridKeyCommand.Clear:
                ClearRequested?.Invoke(this, EventArgs.Empty);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || !GridKeyMap.IsPrintable(e.Text)) return;
        EditRequested?.Invoke(this, e.Text);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        if (p.X < RowHeaderWidth || p.Y < HeaderHeight) return;
        Selection.MoveTo(HitTestCell(p), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        _dragging = CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var a = HitTestCell(e.GetPosition(this));
        Selection.MoveTo(a, extend: true);
        ScrollIntoView(a);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging) ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        int notches = e.Delta / Mouse.MouseWheelDeltaForOneLine;
        if (notches == 0) notches = Math.Sign(e.Delta);
        TopRow -= notches * 3;
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new GridControlAutomationPeer(this);

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    /// <summary>Exposes the grid to UI Automation as a DataGrid; a bare FrameworkElement has no peer (§11.7).</summary>
    private sealed class GridControlAutomationPeer(GridControl owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;

        protected override string GetClassNameCore() => nameof(GridControl);

        protected override bool IsKeyboardFocusableCore() => true;
    }
}
