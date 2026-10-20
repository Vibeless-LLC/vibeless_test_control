using Gridline.Core.Addressing;

namespace Gridline.App;

/// <summary>Which grid header was hit.</summary>
public enum HeaderKind { Row, Column }

/// <summary>UI-free rule for which rows or columns a header context menu acts on (§11.5).</summary>
public static class HeaderTarget
{
    /// <summary>
    /// The first row/column and count a header's Insert/Delete applies to: the selected rows (columns) when the
    /// selection is a full-row (full-column) range and the click lies inside it, otherwise the clicked header only.
    /// </summary>
    public static (int First, int Count) Resolve(HeaderKind kind, int clickedIndex, RangeAddress selection)
    {
        if (kind == HeaderKind.Row)
        {
            bool full = selection.TopLeft.Col == 1 && selection.BottomRight.Col == CellAddress.MaxColumns;
            if (full && clickedIndex >= selection.TopLeft.Row && clickedIndex <= selection.BottomRight.Row)
                return (selection.TopLeft.Row, selection.Rows);
        }
        else
        {
            bool full = selection.TopLeft.Row == 1 && selection.BottomRight.Row == CellAddress.MaxRows;
            if (full && clickedIndex >= selection.TopLeft.Col && clickedIndex <= selection.BottomRight.Col)
                return (selection.TopLeft.Col, selection.Cols);
        }
        return (clickedIndex, 1);
    }
}
