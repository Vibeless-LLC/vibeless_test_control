namespace Gridline.Core.Parsing;

/// <summary>Supplies argument-count limits for known functions (§5.4, §7).</summary>
internal interface IFunctionArity
{
    /// <summary>Returns false if the function is unknown (it then parses fine and evaluates to #NAME?).</summary>
    bool TryGetArity(string upperName, out int min, out int max);
}
