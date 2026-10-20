using Gridline.Core.Addressing;

namespace Gridline.App;

/// <summary>UI-free selection model: an anchor and an active cell spanning a normalized range (§11.3).</summary>
public sealed class GridSelection
{
    public CellAddress Active { get; private set; } = new(1, 1);

    public CellAddress Anchor { get; private set; } = new(1, 1);

    public RangeAddress Range => new(
        new CellAddress(Math.Min(Anchor.Row, Active.Row), Math.Min(Anchor.Col, Active.Col)),
        new CellAddress(Math.Max(Anchor.Row, Active.Row), Math.Max(Anchor.Col, Active.Col)));

    /// <summary>Raised whenever <see cref="Active"/> or <see cref="Anchor"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Makes <paramref name="a"/> (clamped to the grid) active; without <paramref name="extend"/> it is also the anchor.</summary>
    public void MoveTo(CellAddress a, bool extend) => Set(Clamp(a.Row, a.Col), extend);

    /// <summary>Moves the active cell by a delta, clamped to the grid.</summary>
    public void MoveBy(int dRow, int dCol, bool extend) =>
        Set(Clamp((long)Active.Row + dRow, (long)Active.Col + dCol), extend);

    private void Set(CellAddress active, bool extend)
    {
        var anchor = extend ? Anchor : active;
        if (active == Active && anchor == Anchor) return;
        Active = active;
        Anchor = anchor;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static CellAddress Clamp(long row, long col) => new(
        (int)Math.Clamp(row, 1, CellAddress.MaxRows),
        (int)Math.Clamp(col, 1, CellAddress.MaxColumns));
}
