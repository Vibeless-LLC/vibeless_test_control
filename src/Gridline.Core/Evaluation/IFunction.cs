namespace Gridline.Core.Evaluation;

/// <summary>A built-in function (§7). Arity is checked at parse time; arguments are evaluated lazily via <see cref="Arg"/>.</summary>
internal interface IFunction
{
    CellValue Invoke(IReadOnlyList<Arg> args, IEvalContext ctx);
}
