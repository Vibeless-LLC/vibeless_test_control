using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>The §5.3 rule: an omitted argument in a position with no default is #VALUE!.</summary>
internal static class ArgGuard
{
    /// <summary>The argument's scalar value, or #VALUE! if it was omitted.</summary>
    public static CellValue Required(Arg a) =>
        a.IsOmitted ? CellValue.FromError(ErrorCodes.Value) : a.Evaluate();
}
