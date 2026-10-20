using Gridline.App;
using Gridline.Core.Addressing;

namespace Gridline.App.Tests;

public class HeaderSelectionTests
{
    private static RangeAddress R(string s) => RangeAddress.TryParse(s, out var r) ? r : throw new FormatException(s);

    [Fact]
    public void SelectRow_IsFullRow_ActiveInFirstColumn()
    {
        var s = new GridSelection();
        s.SelectRow(5, extend: false);
        Assert.Equal("A5:ZZ5", s.Range.ToString());
        Assert.Equal("A5", s.Active.ToString());
    }

    [Fact]
    public void SelectRow_ShiftExtendsAcrossRows_FromAnchorRow()
    {
        var s = new GridSelection();
        s.SelectRow(5, extend: false);
        s.SelectRow(8, extend: true);
        Assert.Equal("A5:ZZ8", s.Range.ToString());
        s.SelectRow(2, extend: true);
        Assert.Equal("A2:ZZ5", s.Range.ToString());
    }

    [Fact]
    public void SelectRow_ExtendFromCell_UsesAnchorRow()
    {
        var s = new GridSelection();
        s.MoveTo(CellAddress.Parse("C3"), false);
        s.SelectRow(6, extend: true);
        Assert.Equal("A3:ZZ6", s.Range.ToString());
    }

    [Fact]
    public void SelectColumn_IsFullColumn_AndExtends()
    {
        var s = new GridSelection();
        s.SelectColumn(3, extend: false);
        Assert.Equal("C1:C100000", s.Range.ToString());
        s.SelectColumn(5, extend: true);
        Assert.Equal("C1:E100000", s.Range.ToString());
        s.SelectColumn(1, extend: true);
        Assert.Equal("A1:C100000", s.Range.ToString());
    }

    [Fact]
    public void RowTarget_ClickInsideFullRowSelection_UsesSelection()
    {
        var sel = R("A3:ZZ6");
        Assert.Equal((3, 4), HeaderTarget.Resolve(HeaderKind.Row, 5, sel));
    }

    [Fact]
    public void RowTarget_ClickOutsideSelection_UsesClickedOnly()
    {
        var sel = R("A3:ZZ6");
        Assert.Equal((9, 1), HeaderTarget.Resolve(HeaderKind.Row, 9, sel));
    }

    [Fact]
    public void RowTarget_PartialRowSelection_UsesClickedOnly()
    {
        var sel = R("A3:B6");
        Assert.Equal((4, 1), HeaderTarget.Resolve(HeaderKind.Row, 4, sel));
    }

    [Fact]
    public void ColumnTarget_Symmetric()
    {
        var sel = R("B1:D100000");
        Assert.Equal((2, 3), HeaderTarget.Resolve(HeaderKind.Column, 3, sel));
        Assert.Equal((7, 1), HeaderTarget.Resolve(HeaderKind.Column, 7, sel));
        Assert.Equal((3, 1), HeaderTarget.Resolve(HeaderKind.Column, 3, R("B2:D9")));
    }

    [Fact]
    public void FullRowSelection_DoesNotCountAsColumnSelection()
    {
        var sel = R("A3:ZZ6");
        Assert.Equal((2, 1), HeaderTarget.Resolve(HeaderKind.Column, 2, sel));
    }
}
