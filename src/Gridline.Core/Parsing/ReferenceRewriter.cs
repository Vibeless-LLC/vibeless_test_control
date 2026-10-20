using Gridline.Core.Addressing;

namespace Gridline.Core.Parsing;

/// <summary>
/// Rewrites the references of a formula tree: paste offsets (§9.4) and structural insert/delete (§9.5) and sheet renames (CR1.2). The left
/// spine of binary chains is rebuilt iteratively; other nesting recurses once per level (bounded by Parser.MaxDepth).
/// </summary>
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
        var result = Walk(root, new OffsetMap(dRow, dCol), ref ch);
        changed = ch;
        return result;
    }

    /// <summary>
    /// Adjusts every reference for an insert/delete (§9.5); relative and absolute are treated alike and only the
    /// edit's axis moves. A changed <see cref="RefNode"/> keeps its <c>$</c> flags; a failed one becomes
    /// <c>#REF!</c>. A range is normalized first; if its span changes it is re-emitted top-left:bottom-right, each
    /// corner component keeping the <c>$</c> flag of the written corner that supplied it; a failed range becomes
    /// <c>#REF!</c> whole. An unchanged range keeps its corners as written. <paramref name="changed"/> is true if
    /// any reference moved or became <c>#REF!</c>.
    /// </summary>
    public static Node Structural(Node root, StructuralEdit e, out bool changed) =>
        Structural(root, e, static _ => true, out changed);

    /// <summary>
    /// As <see cref="Structural(Node, StructuralEdit, out bool)"/>, but a reference or range is adjusted only when
    /// <paramref name="applies"/> returns true for its <c>Sheet</c> (null when unqualified) (CR1.2).
    /// </summary>
    public static Node Structural(Node root, StructuralEdit e, Func<string?, bool> applies, out bool changed)
    {
        bool ch = false;
        var result = Walk(root, new StructuralMap(e, applies), ref ch);
        changed = ch;
        return result;
    }

    /// <summary>
    /// References and ranges qualified with <paramref name="oldName"/> (OrdinalIgnoreCase) are requalified with
    /// <paramref name="newName"/>, or become <c>#REF!</c> when it is null (CR1.2). Unqualified references are untouched.
    /// </summary>
    public static Node RenameSheet(Node root, string oldName, string? newName, out bool changed)
    {
        bool ch = false;
        var result = Walk(root, new RenameMap(oldName, newName), ref ch);
        changed = ch;
        return result;
    }

    /// <summary>How one rewrite maps leaf references; sets <c>changed</c> when the result differs.</summary>
    private abstract class RefMap
    {
        public abstract Node Ref(RefNode r, ref bool changed);
        public abstract Node Range(RangeNode rg, ref bool changed);
    }

    private sealed class OffsetMap(int dRow, int dCol) : RefMap
    {
        private bool TryShift(RefNode r, out RefNode shifted)
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

        public override Node Ref(RefNode r, ref bool changed)
        {
            if (!TryShift(r, out var s)) { changed = true; return new ErrorNode(RefError); }
            if (!ReferenceEquals(s, r)) changed = true;
            return s;
        }

        public override Node Range(RangeNode rg, ref bool changed)
        {
            if (!TryShift(rg.First, out var f) || !TryShift(rg.Second, out var g))
            {
                changed = true;
                return new ErrorNode(RefError);
            }
            if (ReferenceEquals(f, rg.First) && ReferenceEquals(g, rg.Second)) return rg;
            changed = true;
            return rg with { First = f, Second = g };
        }
    }

    private sealed class StructuralMap(StructuralEdit e, Func<string?, bool> applies) : RefMap
    {
        private readonly int _max = e.Axis == Axis.Row ? CellAddress.MaxRows : CellAddress.MaxColumns;

        public override Node Ref(RefNode r, ref bool changed)
        {
            if (!applies(r.Sheet)) return r;
            int x = e.Axis == Axis.Row ? r.Row : r.Col;
            if (!StructuralShift.TryShiftIndex(x, e, _max, out int nx)) { changed = true; return new ErrorNode(RefError); }
            if (nx == x) return r;
            changed = true;
            return e.Axis == Axis.Row ? r with { Row = nx } : r with { Col = nx };
        }

        public override Node Range(RangeNode rg, ref bool changed)
        {
            if (!applies(rg.Sheet)) return rg;
            RefNode a = rg.First, b = rg.Second;
            // Normalize, remembering which written corner supplied each component (ties: the first corner).
            RefNode rowLo = b.Row < a.Row ? b : a, rowHi = b.Row < a.Row ? a : b;
            RefNode colLo = b.Col < a.Col ? b : a, colHi = b.Col < a.Col ? a : b;
            int r0 = rowLo.Row, r1 = rowHi.Row, c0 = colLo.Col, c1 = colHi.Col;
            int nr0 = r0, nr1 = r1, nc0 = c0, nc1 = c1;
            bool ok = e.Axis == Axis.Row
                ? StructuralShift.TryShiftSpan(r0, r1, e, _max, out nr0, out nr1)
                : StructuralShift.TryShiftSpan(c0, c1, e, _max, out nc0, out nc1);
            if (!ok) { changed = true; return new ErrorNode(RefError); }
            if (nr0 == r0 && nr1 == r1 && nc0 == c0 && nc1 == c1) return rg;
            changed = true;
            return new RangeNode(
                new RefNode(nr0, nc0, rowLo.RowAbs, colLo.ColAbs),
                new RefNode(nr1, nc1, rowHi.RowAbs, colHi.ColAbs),
                rg.Sheet);
        }
    }

    private sealed class RenameMap(string oldName, string? newName) : RefMap
    {
        private bool Matches(string? sheet) => sheet is not null && sheet.Equals(oldName, StringComparison.OrdinalIgnoreCase);

        public override Node Ref(RefNode r, ref bool changed)
        {
            if (!Matches(r.Sheet)) return r;
            changed = true;
            return newName is null ? new ErrorNode(RefError) : r with { Sheet = newName };
        }

        public override Node Range(RangeNode rg, ref bool changed)
        {
            if (!Matches(rg.Sheet)) return rg;
            changed = true;
            return newName is null ? new ErrorNode(RefError) : rg with { Sheet = newName };
        }
    }

    private static Node Walk(Node node, RefMap map, ref bool changed)
    {
        switch (node)
        {
            case RefNode r: return map.Ref(r, ref changed);
            case RangeNode rg: return map.Range(rg, ref changed);
            case UnaryNode u:
                return u with { Operand = Walk(u.Operand, map, ref changed) };
            case PercentNode p:
                return p with { Operand = Walk(p.Operand, map, ref changed) };
            case ParenNode pn:
                return pn with { Inner = Walk(pn.Inner, map, ref changed) };
            case CallNode c:
                return WalkCall(c, map, ref changed);
            case BinaryNode bn:
                return WalkBinary(bn, map, ref changed);
            default:
                return node; // literals and names carry no references
        }
    }

    // Kept out of Walk so its frame stays small: Walk recurses once per nesting level (bounded by Parser.MaxDepth).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Node WalkCall(CallNode c, RefMap map, ref bool changed)
    {
        var args = new Node?[c.Args.Count];
        for (int i = 0; i < args.Length; i++)
            args[i] = c.Args[i] is { } a ? Walk(a, map, ref changed) : null;
        return new CallNode(c.Name, args);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Node WalkBinary(BinaryNode bn, RefMap map, ref bool changed)
    {
        var spine = new List<BinaryNode>();
        Node cur = bn;
        while (cur is BinaryNode b2) { spine.Add(b2); cur = b2.Left; }
        Node acc = Walk(cur, map, ref changed);
        for (int i = spine.Count - 1; i >= 0; i--)
            acc = new BinaryNode(spine[i].Op, acc, Walk(spine[i].Right, map, ref changed));
        return acc;
    }
}
