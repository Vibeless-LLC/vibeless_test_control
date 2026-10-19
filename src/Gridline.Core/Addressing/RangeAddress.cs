namespace Gridline.Core.Addressing;

/// <summary>A normalized rectangular range (TopLeft is the min corner).</summary>
public readonly record struct RangeAddress(CellAddress TopLeft, CellAddress BottomRight)
{
    public int Rows => BottomRight.Row - TopLeft.Row + 1;
    public int Cols => BottomRight.Col - TopLeft.Col + 1;

    public bool Contains(CellAddress a) =>
        a.Row >= TopLeft.Row && a.Row <= BottomRight.Row && a.Col >= TopLeft.Col && a.Col <= BottomRight.Col;

    /// <summary>Accepts ADDR:ADDR or a single ADDR; corners may be in any order.</summary>
    public static bool TryParse(string? s, out RangeAddress r)
    {
        r = default;
        if (s is null) return false;
        var span = s.AsSpan();
        int colon = span.IndexOf(':');
        CellAddress a, b;
        if (colon < 0)
        {
            if (!CellAddress.TryParse(span, out a)) return false;
            b = a;
        }
        else if (!CellAddress.TryParse(span[..colon], out a) || !CellAddress.TryParse(span[(colon + 1)..], out b))
            return false;
        r = new RangeAddress(
            new CellAddress(Math.Min(a.Row, b.Row), Math.Min(a.Col, b.Col)),
            new CellAddress(Math.Max(a.Row, b.Row), Math.Max(a.Col, b.Col)));
        return true;
    }

    public static RangeAddress Parse(string s) =>
        TryParse(s, out var r) ? r : throw new ArgumentException($"Invalid range: '{s}'", nameof(s));

    public override string ToString() =>
        TopLeft == BottomRight ? TopLeft.ToString() : $"{TopLeft}:{BottomRight}";
}
