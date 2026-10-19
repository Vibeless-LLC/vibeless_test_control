using Gridline.Core.Functions;
using Gridline.Core.Parsing;

namespace Gridline.Core.Evaluation;

/// <summary>Exposes a <see cref="FunctionRegistry"/>'s arities to the parser (§5.4).</summary>
internal sealed class FunctionArityAdapter(FunctionRegistry r) : IFunctionArity
{
    public bool TryGetArity(string upperName, out int min, out int max) => r.TryGetArity(upperName, out min, out max);
}
