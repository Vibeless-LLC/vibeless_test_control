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
    private readonly string? _name; // non-null when the argument is a (possibly parenthesized) name

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
                // An undefined name is not a reference: it has no cells, and its value is #NAME?.
                _name = nn.Name;
                if (ctx.TryResolveName(nn.Name, out var target, out bool isRange))
                {
                    IsReference = true;
                    IsRange = isRange;
                    Range = new RangeRef(target, ctx);
                }
                break;
        }
    }

    /// <summary>The argument position was left empty, as in <c>IF(A1,,3)</c>.</summary>
    public bool IsOmitted => _node is null;

    /// <summary>
    /// A cell reference, range reference or defined name, possibly parenthesized (§7.0).
    /// Invariant: <c>IsReference</c> ⇔ <see cref="Range"/> is non-null. An undefined name is not a reference.
    /// </summary>
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
    /// an unresolved name gives <c>#NAME?</c> (or <c>#REF!</c>, §9.6); an omitted argument gives Blank.
    /// </summary>
    public CellValue Evaluate()
    {
        if (!_evaluated)
        {
            _value = _node is null ? CellValue.Blank
                : _name is not null ? EvaluateName() // reuse the ctor's resolution so shape and value agree
                : _evaluator.EvalScalar(_node, _ctx);
            _evaluated = true;
        }
        return _value;
    }

    private CellValue EvaluateName() =>
        Range is null ? _ctx.UnresolvedNameValue(_name!)
        : IsRange ? CellValue.FromError(ErrorCodes.Value)
        : Range.Get(0, 0);
}
