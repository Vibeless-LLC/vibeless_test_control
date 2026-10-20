using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>Wraps a delegate as an <see cref="IFunction"/>.</summary>
internal sealed class DelegateFunction(Func<IReadOnlyList<Arg>, IEvalContext, CellValue> f) : IFunction
{
    public CellValue Invoke(IReadOnlyList<Arg> args, IEvalContext ctx) => f(args, ctx);
}

/// <summary>The built-in function table with arity limits (§5.4, §7).</summary>
internal sealed class FunctionRegistry
{
    private readonly Dictionary<string, IFunction> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Min, int Max)> _arity = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, IFunction> Functions => _functions;

    /// <summary>A registry holding every built-in function implemented so far.</summary>
    public static FunctionRegistry CreateDefault()
    {
        var r = new FunctionRegistry();
        MathFunctions.Register(r);
        LogicFunctions.Register(r);
        TextFunctions.Register(r);
        LookupFunctions.Register(r);
        return r;
    }

    public bool TryGetArity(string upper, out int min, out int max)
    {
        if (_arity.TryGetValue(upper, out var a)) { min = a.Min; max = a.Max; return true; }
        min = max = 0;
        return false;
    }

    /// <summary>Registers a function; used only during construction. <paramref name="name"/> is upper-cased.</summary>
    public void Register(string name, int min, int max, IFunction f)
    {
        string key = name.ToUpperInvariant();
        _functions.Add(key, f);
        _arity.Add(key, (min, max));
    }
}
