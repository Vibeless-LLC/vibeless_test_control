using Gridline.Core.Addressing;
using Gridline.Core.Parsing;

namespace Gridline.Core.Evaluation;

/// <summary>
/// One function argument (§7.0). Reference shape is known up front; the scalar value is computed lazily,
/// at most once.
/// </summary>
internal sealed class Arg
{
    private readonly Node? _node;
    private readonly Evaluator _evaluator;
    private readonly IEvalContext _ctx;
    private CellValue _value;
    private bool _evaluated;

    internal Arg(Node? node, Evaluator evaluator, IEvalContext ctx)
    {
        _node = node;
        _evaluator = evaluator;
        _ctx = ctx;
        if (node is null) return;

        // Parentheses around a reference keep it a reference: SUM((A1:B2)) aggregates the range.
        Node inner = node;
        while (inner is ParenNode p) inner = p.Inner;

        switch (inner)
        {
            case RefNode r:
                IsReference = true;
                var a = new CellAddress(r.Row, r.Col);
                Range = new RangeRef(new RangeAddress(a, a), ctx);
                break;
            case RangeNode rn:
                IsReference = true;
                IsRange = true;
                Range = new RangeRef(Evaluator.Normalize(rn), ctx);
                break;
            case NameNode nn:
                IsReference = true;
                if (ctx.TryResolveName(nn.Name, out var target, out bool isRange))
                {
                    IsRange = isRange;
                    Range = new RangeRef(target, ctx);
                }
                break;
        }
    }

    /// <summary>The argument position was left empty, as in <c>IF(A1,,3)</c>.</summary>
    public bool IsOmitted => _node is null;

    /// <summary>A cell reference, range reference or name, possibly parenthesized (§7.0). Includes undefined names.</summary>
    public bool IsReference { get; }

    /// <summary>A range reference, or a name bound to a range.</summary>
    public bool IsRange { get; }

    /// <summary>
    /// The referenced cells: a 1×1 range for a single-cell reference or name, the range for a range reference or name.
    /// Null for direct arguments, omitted arguments and undefined names.
    /// </summary>
    public RangeRef? Range { get; }

    /// <summary>
    /// The scalar value. A range gives <c>#VALUE!</c>; a reference to a blank cell gives Blank (not 0);
    /// an undefined name gives <c>#NAME?</c>; an omitted argument gives Blank.
    /// </summary>
    public CellValue Evaluate()
    {
        if (!_evaluated)
        {
            _value = _node is null ? CellValue.Blank : _evaluator.EvalScalar(_node, _ctx);
            _evaluated = true;
        }
        return _value;
    }
}
