using System.Runtime.CompilerServices;
using Gridline.Core.Addressing;
using Gridline.Core.Parsing;

namespace Gridline.Core.Evaluation;

/// <summary>Evaluates a parsed formula's AST (§6.3–6.6). Stateless; safe to reuse across formulas.</summary>
/// <param name="functions">Built-in functions keyed by upper-case name. A name not present evaluates to <c>#NAME?</c>.</param>
internal sealed class Evaluator(IReadOnlyDictionary<string, IFunction> functions)
{
    /// <summary>Maximum text length in code units (§4.4).</summary>
    public const int MaxTextLength = 32_767;

    private readonly IReadOnlyDictionary<string, IFunction> _functions = functions;

    /// <summary>
    /// The formula's final value: a parse error gives <c>#PARSE!</c>, a Blank result gives 0, text over
    /// <see cref="MaxTextLength"/> gives <c>#VALUE!</c>, and AST nesting that exhausts the stack gives <c>#VALUE!</c>.
    /// </summary>
    public CellValue Evaluate(ParsedFormula f, IEvalContext ctx)
    {
        if (f.IsParseError || f.Root is null) return CellValue.FromError(ErrorCodes.Parse);
        CellValue v;
        try
        {
            v = EvalScalar(f.Root, ctx);
        }
        catch (InsufficientExecutionStackException)
        {
            return CellValue.FromError(ErrorCodes.Value);
        }
        return v.Kind switch
        {
            ValueKind.Blank => CellValue.FromNumber(0),
            ValueKind.Text when v.Text.Length > MaxTextLength => CellValue.FromError(ErrorCodes.Value),
            _ => v,
        };
    }

    /// <summary>Normalizes a range's corners (as written) to top-left:bottom-right.</summary>
    internal static RangeAddress Normalize(RangeNode r) => new(
        new CellAddress(Math.Min(r.First.Row, r.Second.Row), Math.Min(r.First.Col, r.Second.Col)),
        new CellAddress(Math.Max(r.First.Row, r.Second.Row), Math.Max(r.First.Col, r.Second.Col)));

    /// <summary>
    /// The sheet a reference reads: the formula's own sheet when unqualified, else the named sheet (case-insensitive).
    /// False when the named sheet does not exist; such a reference evaluates to <c>#REF!</c> (CR1.2).
    /// </summary>
    internal bool TryResolveSheet(string? sheetName, IEvalContext ctx, out int sheet)
    {
        if (sheetName is null) { sheet = ctx.FormulaSheet; return true; }
        return ctx.TryResolveSheet(sheetName, out sheet);
    }

    /// <summary>
    /// Scalar value of <paramref name="node"/>. May return Blank (a reference to an empty cell, §6.6);
    /// a range in scalar position gives <c>#VALUE!</c>.
    /// </summary>
    internal CellValue EvalScalar(Node node, IEvalContext ctx)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        while (node is ParenNode p) node = p.Inner;

        switch (node)
        {
            case NumberNode n: return CellValue.FromNumber(n.Value);
            case StringNode s: return CellValue.FromText(s.Value);
            case BoolNode b: return CellValue.FromBoolean(b.Value);
            case ErrorNode e: return CellValue.FromError(e.Code);
            case RefNode r:
                return TryResolveSheet(r.Sheet, ctx, out int rs) ? ctx.GetCellValue(rs, r.Row, r.Col) : CellValue.FromError(ErrorCodes.Ref);
            case RangeNode rn:
                return CellValue.FromError(TryResolveSheet(rn.Sheet, ctx, out _) ? ErrorCodes.Value : ErrorCodes.Ref);
            case NameNode nn:
                if (!ctx.TryResolveName(nn.Name, out int ns, out var target, out bool isRange)) return ctx.UnresolvedNameValue(nn.Name);
                return isRange ? CellValue.FromError(ErrorCodes.Value) : ctx.GetCellValue(ns, target.TopLeft.Row, target.TopLeft.Col);
            case UnaryNode u:
            {
                var v = EvalScalar(u.Operand, ctx);
                if (u.Op == '+') return v; // no coercion (§6.3)
                var num = Coercion.ToNumber(v);
                return num.Kind == ValueKind.Error ? num : CellValue.FromNumber(-num.Number);
            }
            case PercentNode pn:
            {
                var num = Coercion.ToNumber(EvalScalar(pn.Operand, ctx));
                return num.Kind == ValueKind.Error ? num : CellValue.FromNumber(num.Number / 100);
            }
            case BinaryNode bn: return EvalBinary(bn, ctx);
            case CallNode c: return EvalCall(c, ctx);
            default: throw new InvalidOperationException($"Unknown node type {node.GetType().Name}.");
        }
    }

    /// <summary>
    /// Walks the left spine iteratively so long left-associative chains (<c>1+1+…</c>) use constant stack.
    /// </summary>
    private CellValue EvalBinary(BinaryNode b, IEvalContext ctx)
    {
        if (b.Left is not BinaryNode)
            return Apply(b.Op, EvalScalar(b.Left, ctx), b.Right, ctx);

        var spine = new List<BinaryNode>();
        Node cur = b;
        while (cur is BinaryNode bn) { spine.Add(bn); cur = bn.Left; }

        var acc = EvalScalar(cur, ctx);
        for (int i = spine.Count - 1; i >= 0; i--)
            acc = Apply(spine[i].Op, acc, spine[i].Right, ctx);
        return acc;
    }

    /// <summary>
    /// Applies a binary operator. Left-to-right: the left value (and its coercion) is checked before the right
    /// operand is evaluated, so the first error encountered wins (§6.4).
    /// </summary>
    private CellValue Apply(string op, CellValue left, Node rightNode, IEvalContext ctx)
    {
        if (left.Kind == ValueKind.Error) return left;

        switch (op)
        {
            case "&":
            {
                var lt = Coercion.ToText(left);
                var rt = Coercion.ToText(EvalScalar(rightNode, ctx));
                if (rt.Kind == ValueKind.Error) return rt;
                if ((long)lt.Text.Length + rt.Text.Length > MaxTextLength) return CellValue.FromError(ErrorCodes.Value);
                return CellValue.FromText(lt.Text + rt.Text);
            }
            case "=" or "<>" or "<" or ">" or "<=" or ">=":
            {
                var right = EvalScalar(rightNode, ctx);
                if (right.Kind == ValueKind.Error) return right;
                int c = Comparison.Compare(left, right);
                return CellValue.FromBoolean(op switch
                {
                    "=" => c == 0,
                    "<>" => c != 0,
                    "<" => c < 0,
                    ">" => c > 0,
                    "<=" => c <= 0,
                    _ => c >= 0,
                });
            }
        }

        var ln = Coercion.ToNumber(left);
        if (ln.Kind == ValueKind.Error) return ln;
        var rn = Coercion.ToNumber(EvalScalar(rightNode, ctx));
        if (rn.Kind == ValueKind.Error) return rn;
        double x = ln.Number, y = rn.Number;

        switch (op)
        {
            case "+": return CellValue.FromNumber(x + y);
            case "-": return CellValue.FromNumber(x - y);
            case "*": return CellValue.FromNumber(x * y);
            case "/": return y == 0 ? CellValue.FromError(ErrorCodes.Div0) : CellValue.FromNumber(x / y);
            case "^": return Power(x, y);
            default: throw new InvalidOperationException($"Unknown operator '{op}'.");
        }
    }

    internal static CellValue Power(double x, double y)
    {
        if (x == 0 && y == 0) return CellValue.FromError(ErrorCodes.Num);
        if (x == 0 && y < 0) return CellValue.FromError(ErrorCodes.Div0);
        if (x < 0 && y != Math.Truncate(y)) return CellValue.FromError(ErrorCodes.Num);
        return CellValue.FromNumber(Math.Pow(x, y)); // overflow → ∞ → #NUM!
    }

    private CellValue EvalCall(CallNode c, IEvalContext ctx)
    {
        if (!_functions.TryGetValue(c.Name.ToUpperInvariant(), out var fn))
            return CellValue.FromError(ErrorCodes.Name); // args are not evaluated

        var args = new Arg[c.Args.Count];
        for (int i = 0; i < args.Length; i++) args[i] = new Arg(c.Args[i], this, ctx);
        return fn.Invoke(args, ctx);
    }
}
