using System.Text;
using Gridline.Core.Addressing;

namespace Gridline.Core.Parsing;

/// <summary>Canonical formula printing (§5.5). The left spine of binary chains is walked iteratively.</summary>
internal static class FormulaPrinter
{
    /// <summary>
    /// Returns the canonical text including the leading '='. <paramref name="nameCasing"/> receives the
    /// upper-cased name and returns its defined casing, or null to keep the name as written.
    /// </summary>
    /// <remarks>
    /// <paramref name="sheetCasing"/> (CR1.2) receives the upper-cased sheet name of a qualified reference and returns
    /// its defined casing, or null to keep it as written; the name is then quoted only when required.
    /// </remarks>
    public static string Print(Node root, Func<string, string?> nameCasing, Func<string, string?>? sheetCasing = null)
    {
        var sb = new StringBuilder("=");
        Write(sb, root, new Casing(nameCasing, sheetCasing));
        return sb.ToString();
    }

    private sealed record Casing(Func<string, string?> Name, Func<string, string?>? Sheet);

    private static void WriteSheet(StringBuilder sb, string? sheet, Casing casing)
    {
        if (sheet is null) return;
        string name = casing.Sheet?.Invoke(sheet.ToUpperInvariant()) ?? sheet;
        sb.Append(SheetNames.Format(name)).Append('!');
    }

    internal static void WriteRef(StringBuilder sb, RefNode r)
    {
        if (r.ColAbs) sb.Append('$');
        sb.Append(ColumnName.ToLetters(r.Col));
        if (r.RowAbs) sb.Append('$');
        sb.Append(r.Row);
    }

    private static void Write(StringBuilder sb, Node node, Casing casing)
    {
        switch (node)
        {
            case NumberNode or StringNode or BoolNode or ErrorNode or RefNode or RangeNode or NameNode:
                WriteLeaf(sb, node, casing);
                break;
            case UnaryNode u: sb.Append(u.Op); Write(sb, u.Operand, casing); break;
            case PercentNode p: Write(sb, p.Operand, casing); sb.Append('%'); break;
            case ParenNode pn: sb.Append('('); Write(sb, pn.Inner, casing); sb.Append(')'); break;
            case CallNode c: WriteCall(sb, c, casing); break;
            case BinaryNode bn: WriteBinary(sb, bn, casing); break;
            default: throw new InvalidOperationException("Unknown node " + node.GetType().Name);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void WriteCall(StringBuilder sb, CallNode c, Casing casing)
    {
        sb.Append(c.Name.ToUpperInvariant()).Append('(');
        for (int i = 0; i < c.Args.Count; i++)
        {
            if (i > 0) sb.Append(',');
            if (c.Args[i] is { } a) Write(sb, a, casing);
        }
        sb.Append(')');
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void WriteBinary(StringBuilder sb, BinaryNode bn, Casing casing)
    {
        // Iterate the left spine so a 16k-term chain does not overflow the stack.
        var spine = new List<BinaryNode>();
        Node cur = bn;
        while (cur is BinaryNode b2) { spine.Add(b2); cur = b2.Left; }
        Write(sb, cur, casing);
        for (int i = spine.Count - 1; i >= 0; i--)
        {
            sb.Append(spine[i].Op);
            Write(sb, spine[i].Right, casing);
        }
    }

    // Kept out of Write so its frame stays small: Write recurses once per nesting level (bounded by Parser.MaxDepth).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void WriteLeaf(StringBuilder sb, Node node, Casing casing)
    {
        switch (node)
        {
            case NumberNode n: sb.Append(n.Lexeme); break;
            case StringNode s: sb.Append('"').Append(s.Value.Replace("\"", "\"\"")).Append('"'); break;
            case BoolNode b: sb.Append(b.Value ? "TRUE" : "FALSE"); break;
            case ErrorNode e: sb.Append(e.Code.ToUpperInvariant()); break;
            case RefNode r: WriteSheet(sb, r.Sheet, casing); WriteRef(sb, r); break;
            case RangeNode rg: WriteSheet(sb, rg.Sheet, casing); WriteRef(sb, rg.First); sb.Append(':'); WriteRef(sb, rg.Second); break;
            case NameNode nm: sb.Append(casing.Name(nm.Name.ToUpperInvariant()) ?? nm.Name); break;
        }
    }
}
