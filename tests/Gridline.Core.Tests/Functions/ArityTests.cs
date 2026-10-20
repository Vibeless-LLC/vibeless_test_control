using Gridline.Core.Functions;

namespace Gridline.Core.Tests.Functions;

/// <summary>
/// §7.1–7.3 arities, written out from the spec tables (not read back from the registry), checked against the
/// registry and end to end through the parser (§5.4: an out-of-range argument count is <c>#PARSE!</c>).
/// Variadic functions accept at most 255 arguments (§7.0).
/// </summary>
public class ArityTests
{
    private const int V = 255; // variadic maximum (§7.0)

    public static TheoryData<string, int, int> SpecArities => new()
    {
        // §7.1 Math — 20
        { "SUM", 1, V }, { "AVERAGE", 1, V }, { "MIN", 1, V }, { "MAX", 1, V }, { "PRODUCT", 1, V },
        { "COUNT", 1, V }, { "COUNTA", 1, V }, { "COUNTBLANK", 1, 1 }, { "SUMPRODUCT", 1, V },
        { "ABS", 1, 1 }, { "SIGN", 1, 1 }, { "INT", 1, 1 }, { "MOD", 2, 2 }, { "POWER", 2, 2 }, { "SQRT", 1, 1 },
        { "PI", 0, 0 }, { "ROUND", 2, 2 }, { "ROUNDUP", 2, 2 }, { "ROUNDDOWN", 2, 2 }, { "SUMSQ", 1, V },
        // §7.2 Logic and information — 13
        { "IF", 2, 3 }, { "AND", 1, V }, { "OR", 1, V }, { "XOR", 1, V }, { "NOT", 1, 1 },
        { "IFERROR", 2, 2 }, { "IFNA", 2, 2 }, { "ISBLANK", 1, 1 }, { "ISNUMBER", 1, 1 }, { "ISTEXT", 1, 1 },
        { "ISLOGICAL", 1, 1 }, { "ISERROR", 1, 1 }, { "ISNA", 1, 1 },
        // §7.3 Text — 17
        { "LEN", 1, 1 }, { "LEFT", 1, 2 }, { "RIGHT", 1, 2 }, { "MID", 3, 3 }, { "UPPER", 1, 1 }, { "LOWER", 1, 1 },
        { "PROPER", 1, 1 }, { "TRIM", 1, 1 }, { "CONCAT", 1, V }, { "TEXTJOIN", 3, V }, { "FIND", 2, 3 },
        { "SEARCH", 2, 3 }, { "SUBSTITUTE", 3, 4 }, { "REPLACE", 4, 4 }, { "REPT", 2, 2 }, { "EXACT", 2, 2 },
        { "VALUE", 1, 1 },
        // §7.4 Lookup and reference — 9
        { "VLOOKUP", 3, 4 }, { "HLOOKUP", 3, 4 }, { "INDEX", 2, 3 }, { "MATCH", 2, 3 }, { "CHOOSE", 2, V },
        { "ROW", 0, 1 }, { "COLUMN", 0, 1 }, { "ROWS", 1, 1 }, { "COLUMNS", 1, 1 },
    };

    private static string Call(string name, int argCount) =>
        "=" + name + "(" + string.Join(",", Enumerable.Repeat("1", argCount)) + ")";

    [Theory]
    [MemberData(nameof(SpecArities))]
    public void Registry_MatchesSpecArity(string name, int min, int max)
    {
        Assert.True(FunctionRegistry.CreateDefault().TryGetArity(name, out int rMin, out int rMax), name + " not registered");
        Assert.Equal((min, max), (rMin, rMax));
    }

    [Theory]
    [MemberData(nameof(SpecArities))]
    public void Parser_EnforcesArity(string name, int min, int max)
    {
        Assert.NotEqual("E:#PARSE!", CoreTestHost.Eval(Call(name, min)));
        Assert.NotEqual("E:#PARSE!", CoreTestHost.Eval(Call(name, max)));
        if (min > 0) Assert.Equal("E:#PARSE!", CoreTestHost.Eval(Call(name, min - 1)));
        Assert.Equal("E:#PARSE!", CoreTestHost.Eval(Call(name, max + 1)));
    }

    [Fact]
    public void Registry_HasExactlyTheSpecFunctions()
    {
        var expected = SpecArities.Select(row => (string)row[0]).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(59, expected.Length);
        Assert.Equal(expected, FunctionRegistry.CreateDefault().Functions.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Variadic_256Args_IsParseError_255Ok()
    {
        Assert.Equal("E:#PARSE!", CoreTestHost.Eval(Call("SUM", 256)));
        Assert.Equal("N:255", CoreTestHost.Eval(Call("SUM", 255)));
        Assert.Equal("E:#PARSE!", CoreTestHost.Eval(Call("sum", 256)));
    }
}
