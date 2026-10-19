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
    public static string Print(Node root, Func<string, string?> nameCasing)
    {
        var sb = new StringBuilder("=");
        Write(sb, root, nameCasing);
        return sb.ToString();
    }

    internal static void WriteRef(StringBuilder sb, RefNode r)
    {
        if (r.ColAbs) sb.Append('$');
        sb.Append(ColumnName.ToLetters(r.Col));
        if (r.RowAbs) sb.Append('$');
        sb.Append(r.Row);
    }

    private static void Write(StringBuilder sb, Node node, Func<string, string?> nameCasing)
    {
        switch (node)
        {
            case NumberNode n: sb.Append(n.Lexeme); break;
            case StringNode s: sb.Append('"').Append(s.Value.Replace("\"", "\"\"")).Append('"'); break;
            case BoolNode b: sb.Append(b.Value ? "TRUE" : "FALSE"); break;
            case ErrorNode e: sb.Append(e.Code.ToUpperInvariant()); break;
            case RefNode r: WriteRef(sb, r); break;
            case RangeNode rg: WriteRef(sb, rg.First); sb.Append(':'); WriteRef(sb, rg.Second); break;
            case NameNode nm: sb.Append(nameCasing(nm.Name.ToUpperInvariant()) ?? nm.Name); break;
            case UnaryNode u: sb.Append(u.Op); Write(sb, u.Operand, nameCasing); break;
            case PercentNode p: Write(sb, p.Operand, nameCasing); sb.Append('%'); break;
            case ParenNode pn: sb.Append('('); Write(sb, pn.Inner, nameCasing); sb.Append(')'); break;
            case CallNode c:
                sb.Append(c.Name.ToUpperInvariant()).Append('(');
                for (int i = 0; i < c.Args.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (c.Args[i] is { } a) Write(sb, a, nameCasing);
                }
                sb.Append(')');
                break;
            case BinaryNode bn:
                // Iterate the left spine so a 16k-term chain does not overflow the stack.
                var spine = new List<BinaryNode>();
                Node cur = bn;
                while (cur is BinaryNode b2) { spine.Add(b2); cur = b2.Left; }
                Write(sb, cur, nameCasing);
                for (int i = spine.Count - 1; i >= 0; i--)
                {
                    sb.Append(spine[i].Op);
                    Write(sb, spine[i].Right, nameCasing);
                }
                break;
            default: throw new InvalidOperationException("Unknown node " + node.GetType().Name);
        }
    }
}
