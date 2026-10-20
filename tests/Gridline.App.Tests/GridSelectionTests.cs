using Gridline.App;
using Gridline.Core.Addressing;

namespace Gridline.App.Tests;

public class GridSelectionTests
{
    [Fact]
    public void Selection_ExtendAndClamp()
    {
        var s = new GridSelection();
        s.MoveBy(-1, -1, false);
        Assert.Equal("A1", s.Active.ToString());
        s.MoveBy(2, 1, true);
        Assert.Equal("A1:B3", s.Range.ToString());
        s.MoveTo(CellAddress.Parse("ZZ100000"), false);
        s.MoveBy(1, 1, false);
        Assert.Equal("ZZ100000", s.Active.ToString());
    }

    [Fact]
    public void Starts_AtA1_SingleCell()
    {
        var s = new GridSelection();
        Assert.Equal("A1", s.Active.ToString());
        Assert.Equal("A1", s.Anchor.ToString());
        Assert.Equal("A1", s.Range.ToString());
    }

    [Fact]
    public void MoveTo_NoExtend_CollapsesAnchor()
    {
        var s = new GridSelection();
        s.MoveBy(3, 3, true);
        s.MoveTo(CellAddress.Parse("C2"), false);
        Assert.Equal("C2", s.Anchor.ToString());
        Assert.Equal("C2", s.Range.ToString());
    }

    [Fact]
    public void MoveTo_Extend_NormalizesCorners()
    {
        var s = new GridSelection();
        s.MoveTo(CellAddress.Parse("D5"), false);
        s.MoveTo(CellAddress.Parse("B2"), true);
        Assert.Equal("D5", s.Anchor.ToString());
        Assert.Equal("B2", s.Active.ToString());
        Assert.Equal("B2:D5", s.Range.ToString());
    }

    [Fact]
    public void MoveBy_ClampsHugeDeltas()
    {
        var s = new GridSelection();
        s.MoveBy(int.MaxValue, int.MaxValue, false);
        Assert.Equal("ZZ100000", s.Active.ToString());
        s.MoveBy(int.MinValue, int.MinValue, false);
        Assert.Equal("A1", s.Active.ToString());
    }

    [Fact]
    public void Changed_RaisedOnlyWhenSomethingChanges()
    {
        var s = new GridSelection();
        int n = 0;
        s.Changed += (_, _) => n++;
        s.MoveBy(-1, 0, false); // clamped: no change
        Assert.Equal(0, n);
        s.MoveBy(1, 0, false);
        Assert.Equal(1, n);
        s.MoveBy(0, 1, true);
        Assert.Equal(2, n);
        s.MoveTo(s.Active, false); // collapses anchor: change
        Assert.Equal(3, n);
        s.MoveTo(s.Active, false);
        Assert.Equal(3, n);
    }
}
