using Gridline.Core.Addressing;
using Gridline.Core.Dependencies;
using Gridline.Core.Evaluation;
using Gridline.Core.Parsing;

namespace Gridline.Core;

/// <summary>
/// Static dependencies of a parsed formula (§6.1): every cell reference and range (normalized), each with its sheet prefix as written (null = unqualified, CR1.2), and every name in the AST,
/// including those in branches that evaluation may not take. Walks the tree with an explicit stack, so deep
/// nesting cannot overflow the call stack.
/// </summary>
internal static class DependencyExtractor
{
    /// <summary>Null when the formula has a parse error or references nothing.</summary>
    public static FormulaDeps? Extract(ParsedFormula f)
    {
        if (f.IsParseError || f.Root is null) return null;
        List<SheetCell>? cells = null;
        List<SheetRange>? ranges = null;
        List<string>? names = null;

        var stack = new Stack<Node>();
        stack.Push(f.Root);
        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case RefNode r:
                    (cells ??= []).Add(new SheetCell(r.Sheet, CellAddress.KeyOf(r.Row, r.Col)));
                    break;
                case RangeNode rn:
                    (ranges ??= []).Add(new SheetRange(rn.Sheet, Evaluator.Normalize(rn)));
                    break;
                case NameNode nn:
                    (names ??= []).Add(nn.Name.ToUpperInvariant());
                    break;
                case UnaryNode u:
                    stack.Push(u.Operand);
                    break;
                case PercentNode p:
                    stack.Push(p.Operand);
                    break;
                case ParenNode p:
                    stack.Push(p.Inner);
                    break;
                case BinaryNode b:
                    stack.Push(b.Right);
                    stack.Push(b.Left);
                    break;
                case CallNode c:
                    for (int i = c.Args.Count - 1; i >= 0; i--)
                        if (c.Args[i] is { } a) stack.Push(a);
                    break;
                // Number, String, Bool and Error literals have no dependencies.
            }
        }

        if (cells is null && ranges is null && names is null) return null;
        return new FormulaDeps(
            (IReadOnlyList<SheetCell>?)cells ?? [],
            (IReadOnlyList<SheetRange>?)ranges ?? [],
            (IReadOnlyList<string>?)names ?? []);
    }
}
