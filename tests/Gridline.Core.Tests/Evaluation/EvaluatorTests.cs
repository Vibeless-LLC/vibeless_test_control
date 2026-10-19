using System.Globalization;
using System.Text;
using Gridline.Core.Addressing;
using Gridline.Core.Evaluation;
using Gridline.Core.Parsing;

namespace Gridline.Core.Tests.Evaluation;

public class EvaluatorTests
{
    private sealed class NoArity : IFunctionArity
    {
        public bool TryGetArity(string upperName, out int min, out int max) { min = max = 0; return false; }
    }

    /// <summary>Test double: a sparse cell dictionary plus a name table.</summary>
    private sealed class DictContext : IEvalContext
    {
        public Dictionary<(int Row, int Col), CellValue> Cells { get; } = new();
        public Dictionary<string, (RangeAddress Target, bool IsRange)> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int FormulaRow { get; set; } = 1;
        public int FormulaCol { get; set; } = 1;

        public CellValue GetCellValue(int row, int col) => Cells.TryGetValue((row, col), out var v) ? v : CellValue.Blank;

        public bool TryResolveName(string name, out RangeAddress target, out bool isRange)
        {
            if (Names.TryGetValue(name, out var n)) { target = n.Target; isRange = n.IsRange; return true; }
            target = default; isRange = false; return false;
        }

        public IEnumerable<(int Row, int Col, CellValue Value)> EnumerateNonBlank(RangeAddress range) =>
            Cells.Where(kv => kv.Value.Kind != ValueKind.Blank && range.Contains(new CellAddress(kv.Key.Row, kv.Key.Col)))
                 .OrderBy(kv => kv.Key.Row).ThenBy(kv => kv.Key.Col)
                 .Select(kv => (kv.Key.Row, kv.Key.Col, kv.Value));
    }

    private sealed class Fn(Func<IReadOnlyList<Arg>, IEvalContext, CellValue> f) : IFunction
    {
        public CellValue Invoke(IReadOnlyList<Arg> args, IEvalContext ctx) => f(args, ctx);
    }

    private int _ticks;

    private Dictionary<string, IFunction> Functions() => new()
    {
        // Describes each argument: O = omitted; else R/D (reference/direct), range shape or '-', and '*' if IsRange.
        ["PROBE"] = new Fn((args, _) => CellValue.FromText(string.Join(",", args.Select(a =>
            a.IsOmitted ? "O" :
            (a.IsReference ? "R" : "D") + (a.Range is { } r ? $"{r.Rows}x{r.Cols}" : "-") + (a.IsRange ? "*" : ""))))),
        // TRUE iff every arg satisfies IsReference <=> Range != null, and IsRange => IsReference.
        ["INV"] = new Fn((args, _) => CellValue.FromBoolean(args.All(a =>
            a.IsReference == (a.Range is not null) && (!a.IsRange || a.IsReference) && (!a.IsOmitted || !a.IsReference)))),
        ["ECHO"] = new Fn((args, _) => args[0].Evaluate()),
        ["TICK"] = new Fn((_, _) => CellValue.FromNumber(++_ticks)),
        ["TWICE"] = new Fn((args, _) => { args[0].Evaluate(); return args[0].Evaluate(); }),
        ["LAZY"] = new Fn((_, _) => CellValue.FromNumber(1)),
        ["SUMR"] = new Fn((args, _) =>
        {
            double s = 0;
            foreach (var v in args[0].Range!.NonBlankRowMajor())
            {
                if (v.Kind == ValueKind.Error) return v;
                if (v.Kind == ValueKind.Number) s += v.Number;
            }
            return CellValue.FromNumber(s);
        }),
        ["GETR"] = new Fn((args, _) => args[0].Range!.Get((int)args[1].Evaluate().Number, (int)args[2].Evaluate().Number)),
        ["SHAPE"] = new Fn((args, _) =>
        {
            var r = args[0].Range!;
            return CellValue.FromText($"{r.Address}:{r.Rows}x{r.Cols}={r.CellCount}");
        }),
        ["WHERE"] = new Fn((_, ctx) => CellValue.FromText($"{ctx.FormulaRow},{ctx.FormulaCol}")),
    };

    private string Eval(string formula, params (string addr, CellValue v)[] cells) => EvalIn(new DictContext(), formula, cells);

    private string EvalIn(DictContext ctx, string formula, params (string addr, CellValue v)[] cells)
    {
        foreach (var (addr, v) in cells)
        {
            var a = CellAddress.Parse(addr);
            ctx.Cells[(a.Row, a.Col)] = v;
        }
        var parsed = Parser.Parse(formula, new NoArity());
        return new Evaluator(Functions()).Evaluate(parsed, ctx).ToCanonicalString();
    }

    [Theory]
    [InlineData("-2^2", "N:4")] [InlineData("2^3^2", "N:64")] [InlineData("1+2&3", "T:33")] [InlineData("\"a\"&1=1", "B:FALSE")] [InlineData("50%%", "N:0.005")]
    [InlineData("1/0", "E:#DIV/0!")] [InlineData("0^0", "E:#NUM!")] [InlineData("0^-1", "E:#DIV/0!")] [InlineData("(-8)^(1/3)", "E:#NUM!")] [InlineData("10^400", "E:#NUM!")]
    [InlineData("\"2024-01-01\"+0", "E:#VALUE!")] [InlineData("\" 5 \"+1", "N:6")] [InlineData("\"5%\"*1", "N:0.05")] [InlineData("\"\"+1", "E:#VALUE!")]
    [InlineData("TRUE+1", "N:2")] [InlineData("1&TRUE", "T:1TRUE")] [InlineData("0.1+0.2&\"\"", "T:0.3")]
    [InlineData("\"abc\"<\"ABD\"", "B:TRUE")] [InlineData("1<\"a\"", "B:TRUE")] [InlineData("\"a\"<TRUE", "B:TRUE")] [InlineData("\"A\"=\"a\"", "B:TRUE")]
    [InlineData("#N/A+1", "E:#N/A")] [InlineData("#REF!+#N/A", "E:#REF!")] [InlineData("+\"x\"", "T:x")] [InlineData("-\"x\"", "E:#VALUE!")]
    [InlineData("Undefined", "E:#NAME?")] [InlineData("FOO(1/0)", "E:#NAME?")]
    public void Operators(string f, string expected) => Assert.Equal(expected, Eval(f));

    [Theory]
    [InlineData("7-2-1", "N:4")] [InlineData("8/2/2", "N:2")] [InlineData("2*3+4", "N:10")] [InlineData("2+3*4", "N:14")]
    [InlineData("(-8)^(1/1)", "N:-8")] [InlineData("(-2)^3", "N:-8")] [InlineData("0^2", "N:0")] [InlineData("4^0.5", "N:2")]
    [InlineData("-0", "N:0")] [InlineData("0*-1", "N:0")] [InlineData("1e308*10", "E:#NUM!")] [InlineData("\"x\"+#N/A", "E:#VALUE!")]
    [InlineData("1<>1", "B:FALSE")] [InlineData("1<=1", "B:TRUE")] [InlineData("2>=3", "B:FALSE")] [InlineData("2>1", "B:TRUE")]
    [InlineData("TRUE>FALSE", "B:TRUE")] [InlineData("\"10\"<9", "B:FALSE")] [InlineData("#DIV/0!=#N/A", "E:#DIV/0!")]
    [InlineData("1=#N/A", "E:#N/A")] [InlineData("\"a\"&#NUM!", "E:#NUM!")] [InlineData("-TRUE", "N:-1")] [InlineData("+TRUE", "B:TRUE")]
    [InlineData("\"5\"%", "N:0.05")] [InlineData("\"x\"%", "E:#VALUE!")] [InlineData("--\"3\"", "N:3")] [InlineData("1/3&\"\"", "T:0.333333333333333")]
    [InlineData("#PARSE!", "E:#PARSE!")] [InlineData("\"TRUE\"=TRUE", "B:FALSE")] [InlineData("(1+2)*3", "N:9")]
    public void MoreOperators(string f, string expected) => Assert.Equal(expected, Eval(f));

    [Fact] public void BlankRefScalarIsZero() => Assert.Equal("N:0", Eval("A1"));
    [Fact] public void BlankComparesEqualToEmptyText() => Assert.Equal("B:TRUE", Eval("A1=\"\""));
    [Fact] public void BlankComparesEqualToZeroAndFalse() { Assert.Equal("B:TRUE", Eval("A1=0")); Assert.Equal("B:TRUE", Eval("A1=FALSE")); Assert.Equal("B:TRUE", Eval("A1=B1")); }
    [Fact] public void BlankConcat() => Assert.Equal("T:x", Eval("A1&\"x\""));
    [Fact] public void RangeInScalarContext() { Assert.Equal("E:#VALUE!", Eval("A1:B2")); Assert.Equal("E:#VALUE!", Eval("A1:B2+1")); }
    [Fact] public void TextLimit() => Assert.Equal("E:#VALUE!", Eval("A1&A1", ("A1", CellValue.FromText(new string('x', 20000)))));
    [Fact] public void ParseErrorFormula() => Assert.Equal("E:#PARSE!", Eval("1+"));

    [Fact] public void BlankArithmetic() { Assert.Equal("N:1", Eval("A1+1")); Assert.Equal("N:-0.5", Eval("-0.5+A1*3")); Assert.Equal("B:TRUE", Eval("A1<1")); Assert.Equal("B:FALSE", Eval("A1<\"\"")); }
    [Fact] public void BlankNeverFinal() { Assert.Equal("N:0", Eval("(A1)")); Assert.Equal("N:0", Eval("ECHO(A1)")); Assert.Equal("N:0", Eval("+A1")); }
    [Fact] public void BlankFromFunctionAdapts() => Assert.Equal("B:TRUE", Eval("ECHO(A1)=\"\""));
    [Fact] public void TextExactlyAtLimit() => Assert.Equal("T:" + new string('x', 32767), Eval("A1&\"x\"", ("A1", CellValue.FromText(new string('x', 32766)))));
    [Fact] public void TextLimitFromFunction() => Assert.Equal("E:#VALUE!", Eval("ECHO(A1)", ("A1", CellValue.FromText(new string('x', 32768)))));
    [Fact] public void CellValues() => Assert.Equal("N:7", Eval("A1+B2*C3", ("A1", CellValue.FromNumber(1)), ("B2", CellValue.FromNumber(2)), ("C3", CellValue.FromNumber(3))));
    [Fact] public void CellErrorPropagates() => Assert.Equal("E:#N/A", Eval("1+A1", ("A1", CellValue.FromError(ErrorCodes.NA))));
    [Fact] public void TextCellFinal() => Assert.Equal("T:hi", Eval("A1", ("A1", CellValue.FromText("hi"))));

    private DictContext WithNames()
    {
        var ctx = new DictContext();
        ctx.Names["Rng"] = (RangeAddress.Parse("A1:B2"), true);
        ctx.Names["Cell"] = (RangeAddress.Parse("C3"), false);
        return ctx;
    }

    [Fact] public void NameToCell() => Assert.Equal("N:6", EvalIn(WithNames(), "cell*2", ("C3", CellValue.FromNumber(3))));
    [Fact] public void NameToBlankCell() => Assert.Equal("B:TRUE", EvalIn(WithNames(), "Cell=\"\""));
    [Fact] public void NameToRangeInScalar() => Assert.Equal("E:#VALUE!", EvalIn(WithNames(), "Rng"));

    [Fact]
    public void ArgShapes() =>
        Assert.Equal("T:R1x1,R2x3*,D-,O,R2x2*,R1x1,D-,R1x1,D-,R2x2*",
            EvalIn(WithNames(), "PROBE(A1,C3:A2,1+1,,Rng,Cell,Nope,(A1),A1+0,((C3:D4)))"));

    [Fact]
    public void ArgInvariantReferenceHasRange() =>
        Assert.Equal("B:TRUE", EvalIn(WithNames(), "INV(A1,C3:A2,1+1,,Rng,Cell,Nope,(A1),A1+0,((C3:D4)),(Nope),\"x\",FOO(1))"));

    [Fact] public void ArgUndefinedNameParenthesized() => Assert.Equal("E:#NAME?", Eval("ECHO((Nope))"));
    [Fact] public void ArgNameToCellValue() => Assert.Equal("N:3", EvalIn(WithNames(), "ECHO(Cell)", ("C3", CellValue.FromNumber(3))));

    [Fact] public void ArgOmittedOnly() => Assert.Equal("T:O,O", Eval("PROBE(,)"));
    [Fact] public void ArgNoArgs() => Assert.Equal("T:", Eval("PROBE()"));
    [Fact] public void ArgRangeNormalized() => Assert.Equal("T:A1:C4:4x3=12", Eval("SHAPE(C4:A1)"));
    [Fact] public void ArgSingleCellRange() => Assert.Equal("T:B2:1x1=1", Eval("SHAPE(B2)"));
    [Fact] public void ArgNameRange() => Assert.Equal("T:A1:B2:2x2=4", EvalIn(WithNames(), "SHAPE(Rng)"));
    [Fact] public void ArgScalarOfRangeIsValue() => Assert.Equal("E:#VALUE!", Eval("ECHO(A1:B2)"));
    [Fact] public void ArgScalarOfNameRangeIsValue() => Assert.Equal("E:#VALUE!", EvalIn(WithNames(), "ECHO(Rng)"));
    [Fact] public void ArgUndefinedName() => Assert.Equal("E:#NAME?", Eval("ECHO(Nope)"));
    [Fact] public void ArgBlankRefStaysBlank() => Assert.Equal("B:TRUE", Eval("ECHO(A1)=FALSE"));

    [Fact] public void ArgEvaluatedOnce() { Assert.Equal("N:1", Eval("TWICE(TICK())")); Assert.Equal(1, _ticks); }
    [Fact] public void ArgLazy() { Assert.Equal("N:2", Eval("LAZY(TICK())+TICK()")); Assert.Equal(1, _ticks); }
    [Fact] public void UnknownFunctionArgsNotEvaluated() { Assert.Equal("E:#NAME?", Eval("FOO(TICK())")); Assert.Equal(0, _ticks); }
    [Fact] public void FunctionNameCaseInsensitive() => Assert.Equal("N:5", Eval("echo(5)"));
    [Fact] public void FunctionSeesContext() { var ctx = new DictContext { FormulaRow = 7, FormulaCol = 3 }; Assert.Equal("T:7,3", EvalIn(ctx, "WHERE()")); }

    [Fact]
    public void RangeRefNonBlankAndGet()
    {
        var cells = new[]
        {
            ("A1", CellValue.FromNumber(1)), ("B1", CellValue.FromText("x")), ("A2", CellValue.FromNumber(10)), ("C9", CellValue.FromNumber(100)),
        };
        Assert.Equal("N:11", Eval("SUMR(A1:B2)", cells));
        Assert.Equal("N:10", Eval("GETR(A1:B2,1,0)", cells));
        Assert.Equal("N:0", Eval("GETR(A1:B2,1,1)", cells)); // blank cell -> Blank, final maps to 0
        Assert.Equal("E:#N/A", Eval("SUMR(A1:A3)", ("A1", CellValue.FromNumber(1)), ("A3", CellValue.FromError(ErrorCodes.NA))));
    }

    [Fact]
    public void RangeRefGetOutOfBoundsThrows()
    {
        var r = new RangeRef(RangeAddress.Parse("A1:B2"), new DictContext());
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Get(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Get(0, -1));
    }

    [Fact]
    public void DeepLeftChainDoesNotCrash()
    {
        string f = "1" + string.Concat(Enumerable.Repeat("+1", 15999));
        string r = Eval(f);
        Assert.Equal("N:16000", r); // preferred: the left spine is evaluated iteratively
    }

    [Fact]
    public void DeepUnaryNestingDoesNotCrash()
    {
        // Build the AST directly (the parser may itself reject very deep nesting) and check exhaustion maps to #VALUE!.
        Node n = new NumberNode(1, "1");
        for (int i = 0; i < 200_000; i++) n = new UnaryNode('-', n);
        var r = new Evaluator(Functions()).Evaluate(new ParsedFormula(n, false), new DictContext());
        Assert.Equal("E:#VALUE!", r.ToCanonicalString());
    }

    [Fact]
    public void DeepRightChainDoesNotCrash()
    {
        Node n = new NumberNode(1, "1");
        for (int i = 0; i < 200_000; i++) n = new BinaryNode("+", new NumberNode(1, "1"), new ParenNode(n));
        var r = new Evaluator(Functions()).Evaluate(new ParsedFormula(n, false), new DictContext());
        Assert.Equal("E:#VALUE!", r.ToCanonicalString());
    }
}
