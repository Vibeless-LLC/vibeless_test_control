using Gridline.Core.Addressing;

namespace Gridline.Core.Tests.Addressing;

/// <summary>Index and span adjustment under insert/delete (§9.5).</summary>
public class StructuralShiftTests
{
    [Theory]
    [InlineData(true, 3, 2, 1, 5, 1, 7)] [InlineData(true, 1, 2, 1, 5, 3, 7)] [InlineData(false, 3, 2, 1, 10, 1, 8)] [InlineData(false, 1, 2, 1, 10, 1, 8)] [InlineData(false, 5, 6, 3, 7, 3, 4)]
    public void Span(bool ins, int at, int n, int lo, int hi, int elo, int ehi) { Assert.True(StructuralShift.TryShiftSpan(lo, hi, new(Axis.Row, at, n, ins), 100000, out var a, out var b)); Assert.Equal((elo, ehi), (a, b)); }
    [Fact] public void Span_FullyDeleted_Fails() => Assert.False(StructuralShift.TryShiftSpan(3, 4, new(Axis.Row, 3, 2, false), 100000, out _, out _));
    [Fact] public void Index_DeletedFails_InsertOverflowFails() { Assert.False(StructuralShift.TryShiftIndex(4, new(Axis.Row, 3, 2, false), 100000, out _)); Assert.False(StructuralShift.TryShiftIndex(99999, new(Axis.Row, 1, 5, true), 100000, out _)); }

    [Theory]
    [InlineData(true, 3, 2, 2, 2)] [InlineData(true, 3, 2, 3, 5)] [InlineData(false, 3, 2, 2, 2)] [InlineData(false, 3, 2, 5, 3)] [InlineData(true, 1, 100000, 1, -1)] [InlineData(true, 1, 99999, 1, 100000)]
    public void Index(bool ins, int at, int n, int x, int expected)
    {
        if (expected < 0) { Assert.False(StructuralShift.TryShiftIndex(x, new(Axis.Row, at, n, ins), 100000, out _)); return; }
        Assert.True(StructuralShift.TryShiftIndex(x, new(Axis.Row, at, n, ins), 100000, out int r));
        Assert.Equal(expected, r);
    }

    [Fact] public void Index_HugeCount_NoOverflow() => Assert.False(StructuralShift.TryShiftIndex(5, new(Axis.Row, 1, int.MaxValue, true), 100000, out _));

    [Fact] public void Span_InsertClampsHigh_FailsWhenLowPast()
    {
        Assert.True(StructuralShift.TryShiftSpan(99990, 100000, new(Axis.Row, 2, 5, true), 100000, out var lo, out var hi));
        Assert.Equal((99995, 100000), (lo, hi));
        Assert.False(StructuralShift.TryShiftSpan(99997, 100000, new(Axis.Row, 2, 5, true), 100000, out _, out _));
    }

    [Theory]
    [InlineData(3, 2, 1, 4, 1, 2)] [InlineData(3, 2, 4, 9, 3, 7)] [InlineData(1, 2, 2, 2, 0, 0)]
    public void Span_DeleteEdges(int at, int n, int lo, int hi, int elo, int ehi)
    {
        bool ok = StructuralShift.TryShiftSpan(lo, hi, new(Axis.Row, at, n, false), 100000, out var a, out var b);
        if (elo == 0) { Assert.False(ok); return; }
        Assert.True(ok);
        Assert.Equal((elo, ehi), (a, b));
    }
}
