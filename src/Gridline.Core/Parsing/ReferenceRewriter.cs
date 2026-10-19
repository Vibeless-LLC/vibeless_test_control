using Gridline.Core.Addressing;

namespace Gridline.Core.Parsing;

/// <summary>Shifts relative references by a paste offset (§9.4). The left spine of binary chains is rebuilt iteratively.</summary>
internal static class ReferenceRewriter
{
    private const string RefError = "#REF!";

    /// <summary>
    /// Relative components move by (dRow, dCol); '$' components stay. A reference leaving the grid becomes
    /// <c>#REF!</c>; a range with either corner invalid becomes <c>#REF!</c> whole. <paramref name="changed"/>
    /// is true if any reference moved or became <c>#REF!</c>.
    /// </summary>
    public static Node Shift(Node root, int dRow, int dCol, out bool changed)
    {
        bool ch = false;
        var result = Walk(root, dRow, dCol, ref ch);
        changed = ch;
        return result;
    }

    private static bool TryShift(RefNode r, int dRow, int dCol, out RefNode shifted)
    {
        long row = r.RowAbs ? r.Row : (long)r.Row + dRow;
        long col = r.ColAbs ? r.Col : (long)r.Col + dCol;
        if (row < 1 || row > CellAddress.MaxRows || col < 1 || col > CellAddress.MaxColumns)
        {
            shifted = r;
            return false;
        }
        shifted = (row == r.Row && col == r.Col) ? r : r with { Row = (int)row, Col = (int)col };
        return true;
    }

    private static Node Walk(Node node, int dRow, int dCol, ref bool changed)
    {
        switch (node)
        {
            case RefNode r: return ShiftRef(r, dRow, dCol, ref changed);
            case RangeNode rg: return ShiftRange(rg, dRow, dCol, ref changed);
            case UnaryNode u:
                return u with { Operand = Walk(u.Operand, dRow, dCol, ref changed) };
            case PercentNode p:
                return p with { Operand = Walk(p.Operand, dRow, dCol, ref changed) };
            case ParenNode pn:
                return pn with { Inner = Walk(pn.Inner, dRow, dCol, ref changed) };
            case CallNode c:
                return WalkCall(c, dRow, dCol, ref changed);
            case BinaryNode bn:
                return WalkBinary(bn, dRow, dCol, ref changed);
            default:
                return node; // literals and names carry no references
        }
    }

    // Kept out of Walk so its frame stays small: Walk recurses once per nesting level (bounded by Parser.MaxDepth).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Node ShiftRef(RefNode r, int dRow, int dCol, ref bool changed)
    {
        if (!TryShift(r, dRow, dCol, out var s)) { changed = true; return new ErrorNode(RefError); }
        if (!ReferenceEquals(s, r)) changed = true;
        return s;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Node ShiftRange(RangeNode rg, int dRow, int dCol, ref bool changed)
    {
        if (!TryShift(rg.First, dRow, dCol, out var f) || !TryShift(rg.Second, dRow, dCol, out var g))
        {
            changed = true;
            return new ErrorNode(RefError);
        }
        if (ReferenceEquals(f, rg.First) && ReferenceEquals(g, rg.Second)) return rg;
        changed = true;
        return new RangeNode(f, g);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Node WalkCall(CallNode c, int dRow, int dCol, ref bool changed)
    {
        var args = new Node?[c.Args.Count];
        for (int i = 0; i < args.Length; i++)
            args[i] = c.Args[i] is { } a ? Walk(a, dRow, dCol, ref changed) : null;
        return new CallNode(c.Name, args);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Node WalkBinary(BinaryNode bn, int dRow, int dCol, ref bool changed)
    {
        var spine = new List<BinaryNode>();
        Node cur = bn;
        while (cur is BinaryNode b2) { spine.Add(b2); cur = b2.Left; }
        Node acc = Walk(cur, dRow, dCol, ref changed);
        for (int i = spine.Count - 1; i >= 0; i--)
            acc = new BinaryNode(spine[i].Op, acc, Walk(spine[i].Right, dRow, dCol, ref changed));
        return acc;
    }
}
