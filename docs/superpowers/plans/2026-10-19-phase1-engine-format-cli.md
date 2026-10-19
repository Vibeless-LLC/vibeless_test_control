# Gridline Phase 1 — Engine, File Format, CLI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Phase 1 scope from spec §13: the formula engine (§4, §5.1–5.4, §6, §7.1–7.3), the `.gridline` format (§8.1–8.4), the Phase 1 public API (§9.1, §9.2), the Phase 1 CLI (§10), performance budgets P-1–P-5 and P-7, and the architecture tests (§3).

**Architecture:** `Gridline.Core` holds a dictionary-plus-column-index cell store, a recursive-descent parser that produces an immutable AST, a tree-walking evaluator whose functions take lazy argument thunks, and a dependency graph. The graph recalculates through an iterative Tarjan SCC pass, so it detects cycles without recursion. `Gridline.IO` writes the file format byte-for-byte by hand and reads it with `System.Text.Json`. `Gridline.Cli` is a thin `Run(args, stdin, stdout, stderr)` entry point, which keeps it testable in-process.

**Tech Stack:** .NET 10, C# latest, xUnit, FsCheck.Xunit, NetArchTest.Rules. No packages in `src/`.

**Spec:** `.claude/architecture.md`. Every task cites the `§` it implements. Read the cited sections before you start a task.

## Global Constraints

- `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in every project (put these in `Directory.Build.props`).
- Core/IO/Cli target `net10.0`. App (Phase 2) targets `net10.0-windows` with `<UseWPF>true</UseWPF>`.
- `src/` has **zero** NuGet packages. Tests may use only: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FsCheck.Xunit`, `NetArchTest.Rules`, `FlaUI.UIA3`, `coverlet.collector`.
- The Cli project sets `<AssemblyName>gridline</AssemblyName>`.
- Core MUST NOT use `System.IO.File`, `Directory`, `FileStream`, `Console`, or `System.Windows*`. IO MUST NOT use `Console`.
- No public mutable static state anywhere in `src/`.
- Every culture-sensitive call uses `CultureInfo.InvariantCulture` or ordinal comparison.
- Grid bounds: columns 1–702 (`A`–`ZZ`), rows 1–100000. Max input is 32,767 UTF-16 code units.
- Verification for every task: `dotnet build Gridline.sln -c Release` has zero errors and warnings, and the named tests pass.
- Commit after every task. Messages cite the spec sections, e.g. `feat(core): tokenizer (§5.1)`, and end with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Record every non-trivial choice in `DECISIONS.md` with the related `§` in the same commit.

## Review Focus

1. **A cycle broken by editing one member.** For example, `A1=B1`, `B1=A1`, then `SetCell("B1","5")`. `A1` must go back to `5`, and a third cell `C1=A1+1` must become `6`. Test: `Workbook_BreakingCycle_RestoresAllMembersAndDependents` (Task 10).
2. **Long chains through ranges, not just single refs.** For example, `A{n}=SUM(A$1:A{n-1})` for 20,000 rows, which also makes edges dense. These MUST neither recurse nor go quadratic. Test: `Recalc_RangeChain_NoStackOverflow` (Task 9).
3. **Control characters and non-ASCII in inputs** (tab, CR, LF, U+0001, `é`, emoji). They must survive save → load byte-exactly and print correctly via `INPUT`/`SETX`. Tests: `Save_EscapesControlCharsExactly` (Task 11) and `Script_SetxInput_RoundTripsEscapes` (Task 12).
4. **Word-token edge cases.** `A0`, `ZZZ1`, `AAA1` and `A100001` are names, so they give `#NAME?`. `$A0`, `A$0` and `$Foo` give `#PARSE!`. `SUM (A1)` gives `#PARSE!`. `sum(a1)` works. Test: `Tokenizer_WordTokenClassification` theory (Task 3) and `Eval_WordTokenEdgeCases` (Task 10).
5. **Invalid addresses through every entry point.** These are `GetValue("A0")`, `SetCell("ZZZ1","x")` and CLI `eval f.gridline A0`. Each throws `ArgumentException` with no change, and the CLI exits 5. Tests: `Workbook_InvalidAddress_ThrowsAndChangesNothing` (Task 10) and `Eval_InvalidAddress_Exit5` (Task 12).

---

## File Structure

```
Gridline.sln
Directory.Build.props            shared Nullable/LangVersion/TreatWarningsAsErrors
.gitignore                       bin/ obj/ *.user .vs/
DECISIONS.md
src/Gridline.Core/
  ValueKind.cs ErrorCodes.cs CellValue.cs RecalcStats.cs CellsChangedEventArgs.cs
  Workbook.cs                    public façade (§9.1); owns CellStore, graph, evaluator
  InputInterpreter.cs            §4.3 classification (internal)
  CellStore.cs                   internal: Dictionary<int,Cell> + per-column sorted row index
  Cell.cs                        internal: Input, Kind, Ast?, Value
  Addressing/CellAddress.cs      readonly record struct; parse/format/key
  Addressing/RangeAddress.cs     readonly record struct; normalized corners
  Addressing/ColumnName.cs       letters <-> number
  Parsing/Token.cs Tokenizer.cs
  Parsing/Ast.cs                 node records
  Parsing/Parser.cs              recursive descent; IFunctionArity
  Parsing/IFunctionArity.cs
  Evaluation/NumberGrammar.cs    §4.3 rule 5 (shared by input, coercion, criteria)
  Evaluation/Coercion.cs         §6.2
  Evaluation/Comparison.cs       §6.3 comparison ordering
  Evaluation/IEvalContext.cs     cell/name access for the evaluator
  Evaluation/Arg.cs              lazy function argument (scalar | range | omitted)
  Evaluation/RangeRef.cs         a range view used by functions
  Evaluation/IFunction.cs        function contract
  Evaluation/Evaluator.cs        AST walker
  Evaluation/FunctionArityAdapter.cs  IFunctionArity over FunctionRegistry
  Functions/FunctionRegistry.cs
  Functions/Aggregation.cs       §7.0 agg rules helper
  Functions/MathFunctions.cs LogicFunctions.cs TextFunctions.cs
  Functions/Wildcard.cs          * ? ~ matcher (SEARCH; reused in Phase 3)
  Functions/Rounding.cs          decimal-string rounding for ROUND*
  Dependencies/DependencyGraph.cs
  Dependencies/RecalcPlanner.cs  iterative Tarjan SCC + topo order
src/Gridline.IO/
  GridlineFile.cs                Serialize / Load / SaveToPath / LoadFromPath
  GridlineFormatException.cs
  JsonEscaper.cs                 §8.1 escaping
src/Gridline.Cli/
  Program.cs                     Main → CliApp.Run with UTF-8/LF writers
  CliApp.cs                      command dispatch, exit codes
  ScriptRunner.cs                §10.3
  Escaping.cs                    §8.3 escape / SETX unescape
tests/Gridline.Core.Tests/ Gridline.IO.Tests/ Gridline.Cli.Tests/ Gridline.Architecture.Tests/
```

---

### Task 1: Solution scaffold and Addressing (§2, §3.1, §4.1, §4.2)

**Files:**
- Create: `Gridline.sln`, `Directory.Build.props`, `.gitignore`, `DECISIONS.md`, all four `src/` projects except App (App comes in Phase 2), and the five test projects except `Gridline.App.Tests`. Each test project references its `src` project.
- Create: `src/Gridline.Core/Addressing/{CellAddress,RangeAddress,ColumnName}.cs`
- Test: `tests/Gridline.Core.Tests/Addressing/AddressTests.cs`

**Interfaces:**
- Produces (all `public` in `Gridline.Core.Addressing`, with no dependency on any other `Gridline.Core*` namespace):
  - `static class ColumnName { string ToLetters(int col); bool TryParse(ReadOnlySpan<char> letters, out int col); }`
  - `readonly record struct CellAddress(int Row, int Col)` with these members:
    - `bool RowAbsolute`/`ColAbsolute` are **not** stored here. `$` belongs to AST refs.
    - `static bool TryParse(string s, out CellAddress a)` accepts `[$]COL[$]ROW` case-insensitively, within bounds.
    - `static CellAddress Parse(string s)` throws `ArgumentException`.
    - `int Key => Row * 1024 + Col`, which sorts row-major. `static CellAddress FromKey(int key)`.
    - `override string ToString()` returns the canonical form, e.g. `B7`.
    - `const int MaxRows = 100_000, MaxColumns = 702`.
  - `readonly record struct RangeAddress(CellAddress TopLeft, CellAddress BottomRight)` with these members:
    - `static bool TryParse(string s, out RangeAddress r)` accepts `ADDR:ADDR` **or a single ADDR**, and normalizes any corner order.
    - `Parse` throws `ArgumentException`.
    - `int Rows`, `int Cols`, `bool Contains(CellAddress)`.
    - `ToString()` returns `A1:B3`, or `A1` when the range is 1×1.

- [ ] **Step 1:** Scaffold with `dotnet new sln -n Gridline`, `dotnet new classlib`/`console`/`xunit` and `dotnet sln add`. Remove template `Class1.cs`/`UnitTest1.cs`. Run `git init` and make the first commit of the scaffold, including `.claude/` but excluding `bin`/`obj`.
- [ ] **Step 2: Write failing tests**

```csharp
[Theory]
[InlineData("a1", 1, 1)] [InlineData("$B$7", 7, 2)] [InlineData("zz100000", 100000, 702)] [InlineData("Az12", 12, 52)]
public void CellAddress_Parses(string s, int row, int col) { Assert.True(CellAddress.TryParse(s, out var a)); Assert.Equal(new CellAddress(row, col), a); }

[Theory]
[InlineData("A0")] [InlineData("A100001")] [InlineData("AAA1")] [InlineData("")] [InlineData("1A")] [InlineData("A")] [InlineData("A1 ")] [InlineData("$$A1")] [InlineData("A$$1")]
public void CellAddress_RejectsInvalid(string s) => Assert.False(CellAddress.TryParse(s, out _));

[Fact] public void CellAddress_CanonicalForm() => Assert.Equal("B7", CellAddress.Parse("$b$7").ToString());
[Fact] public void Range_Normalizes() => Assert.Equal("A1:B3", RangeAddress.Parse("B3:A1").ToString());
[Fact] public void Range_NormalizesMixedCorners() => Assert.Equal("A1:B3", RangeAddress.Parse("A3:B1").ToString());
[Fact] public void Key_SortsRowMajor() => Assert.True(CellAddress.Parse("ZZ1").Key < CellAddress.Parse("A2").Key);
[Fact] public void ColumnName_RoundTrips() { for (int c = 1; c <= 702; c++) { Assert.True(ColumnName.TryParse(ColumnName.ToLetters(c), out var back)); Assert.Equal(c, back); } }
```

- [ ] **Step 3:** Run `dotnet test tests/Gridline.Core.Tests -c Release --filter AddressTests`. Expected: the build fails because the types are missing.
- [ ] **Step 4:** Implement the types. Parsing is hand-written span scanning, not regex. Reject a row with a leading `0`, such as `A01`? Excel accepts `A01`, so **accept** it and record that in DECISIONS.md.
- [ ] **Step 5:** Run the tests. Expected: PASS. Then commit: `feat(core): solution scaffold and addressing (§3.1, §4.1, §4.2)`.

---

### Task 2: Value types (§4.4, §4.5, §8.3, §9.1)

**Files:**
- Create: `src/Gridline.Core/{ValueKind,ErrorCodes,CellValue,RecalcStats,CellsChangedEventArgs}.cs`
- Test: `tests/Gridline.Core.Tests/CellValueTests.cs`

**Interfaces:**
- Produces: exactly the §9.1 declarations, plus these:
  - `CellValue.ToString()` delegates to `ToCanonicalString()`.
  - `CellsChangedEventArgs(IReadOnlyCollection<string> addresses)` is a public constructor.
  - `ErrorCodes.All` is an `internal static readonly FrozenSet<string>` (immutable, so it is not mutable static state).
  - `ErrorCodes.IsValid(string)` is `public static bool`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void FromNumber_NegativeZeroNormalized() { var v = CellValue.FromNumber(-0.0); Assert.Equal(0.0, v.Number); Assert.False(double.IsNegative(v.Number)); Assert.Equal("N:0", v.ToCanonicalString()); }
[Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
public void FromNumber_NonFinite_IsNum(double d) => Assert.Equal("E:#NUM!", CellValue.FromNumber(d).ToCanonicalString());
[Fact] public void FromError_Unknown_Throws() => Assert.Throws<ArgumentException>(() => CellValue.FromError("#BAD!"));
[Fact] public void Canonical_Text_Escapes() => Assert.Equal("T:a\\\\b\\tc\\nd\\re", CellValue.FromText("a\\b\tc\nd\re").ToCanonicalString());
[Fact] public void Canonical_Number_UsesR() => Assert.Equal("N:0.1", CellValue.FromNumber(0.1).ToCanonicalString());
[Fact] public void Canonical_Others() { Assert.Equal("BLANK", CellValue.Blank.ToCanonicalString()); Assert.Equal("B:TRUE", CellValue.FromBoolean(true).ToCanonicalString()); Assert.Equal("E:#DIV/0!", CellValue.FromError(ErrorCodes.Div0).ToCanonicalString()); }
[Fact] public void DefaultFields() { var t = CellValue.FromText("x"); Assert.Equal(0, t.Number); Assert.Equal("", t.Error); Assert.False(t.Boolean); Assert.Equal("", CellValue.Blank.Text); }
[Fact] public void Equality() { Assert.Equal(CellValue.FromText("a"), CellValue.FromText("a")); Assert.NotEqual(CellValue.FromText("a"), CellValue.FromText("A")); }
```

- [ ] **Step 2:** Run with `--filter CellValueTests`. Expected: the build fails.
- [ ] **Step 3:** Implement. `default(CellValue)` MUST behave as Blank, with `Text`/`Error` returning `""` rather than null. Back the string properties with `_text ?? ""`.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): CellValue and public value types (§4.4, §8.3, §9.1)`.

---

### Task 3: Tokenizer (§5.1)

**Files:**
- Create: `src/Gridline.Core/Parsing/{Token,Tokenizer}.cs`
- Test: `tests/Gridline.Core.Tests/Parsing/TokenizerTests.cs`

**Interfaces:**
- Produces (all `internal`; Core.Tests gets `InternalsVisibleTo`):
  - `enum TokenKind { Number, String, Error, Function, CellRef, Bool, Name, Op, LParen, RParen, Comma, End }`
  - `readonly record struct Token(TokenKind Kind, string Text, int Pos)`:
    - For `Number`, `Text` is the exact lexeme.
    - For `String`, `Text` is the unescaped value.
    - For `Function`, `Text` is the name as written, without the `(`.
    - For `CellRef`, `Text` is the lexeme with `$`.
    - For `Op`, `Text` is one of `: + - * / ^ & % = <> < > <= >=`.
  - `static class Tokenizer { static bool TryTokenize(string formulaBody, out List<Token> tokens); }`. It returns false on any lexical error: an unterminated string, a stray character, an invalid `$` token, or an unknown `#...` error literal. `formulaBody` excludes the leading `=`.

Word-token rule: scan `[$]?[A-Za-z][A-Za-z0-9_.]*`, and if a `$` follows, also scan `$\d+`. A `(` **immediately** after the word makes it a Function, unless the word contains `$`, which is a lexical error. Otherwise the word becomes a CellRef if `CellAddress.TryParse` succeeds, then a Bool, then a Name. A word containing `$` that is not a valid CellRef makes `TryTokenize` return false. Error literals are matched longest-first from `ErrorCodes`.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData("A1", TokenKind.CellRef)] [InlineData("$a$1", TokenKind.CellRef)] [InlineData("ZZ100000", TokenKind.CellRef)]
[InlineData("A0", TokenKind.Name)] [InlineData("ZZZ1", TokenKind.Name)] [InlineData("A100001", TokenKind.Name)] [InlineData("Rate", TokenKind.Name)] [InlineData("my.name_1", TokenKind.Name)]
[InlineData("true", TokenKind.Bool)] [InlineData("FALSE", TokenKind.Bool)]
public void Tokenizer_WordTokenClassification(string src, TokenKind kind) { Assert.True(Tokenizer.TryTokenize(src, out var t)); Assert.Equal(kind, t[0].Kind); }

[Theory] [InlineData("$A0")] [InlineData("A$0")] [InlineData("$Foo")] [InlineData("\"abc")] [InlineData("#BOGUS!")] [InlineData("A1 ~ 2")] [InlineData("$SUM(1)")]
public void Tokenizer_LexicalErrors(string src) => Assert.False(Tokenizer.TryTokenize(src, out _));

[Fact] public void FunctionRequiresAdjacentParen() { Tokenizer.TryTokenize("SUM (A1)", out var t); Assert.Equal(TokenKind.Name, t[0].Kind); Assert.Equal(TokenKind.LParen, t[1].Kind); }
[Fact] public void StringDoubledQuote() { Tokenizer.TryTokenize("\"a\"\"b\"", out var t); Assert.Equal("a\"b", t[0].Text); }
[Fact] public void NumberLexemeKept() { Tokenizer.TryTokenize("1.50+.5e3", out var t); Assert.Equal("1.50", t[0].Text); Assert.Equal(".5e3", t[2].Text); }
[Fact] public void TwoCharOps() { Tokenizer.TryTokenize("1<=2<>3>=4", out var t); Assert.Equal(new[]{"<=","<>",">="}, t.Where(x=>x.Kind==TokenKind.Op).Select(x=>x.Text)); }
[Fact] public void ErrorLiteral() { Tokenizer.TryTokenize("#DIV/0!+#N/A", out var t); Assert.Equal("#DIV/0!", t[0].Text); Assert.Equal("#N/A", t[2].Text); }
[Fact] public void WhitespaceSkipped() { Assert.True(Tokenizer.TryTokenize(" \t1 + 2 ", out var t)); Assert.Equal(4, t.Count); }
```

`"SUM (A1)"` tokenizes as Name + LParen. The parser rejects that sequence, which gives `#PARSE!`.

- [ ] **Step 2:** Run with `--filter TokenizerTests`. Expected: FAIL (the build fails).
- [ ] **Step 3:** Implement the tokenizer.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): tokenizer (§5.1)`.

---

### Task 4: AST and parser (§5.2–5.4)

**Files:**
- Create: `src/Gridline.Core/Parsing/{Ast,Parser,IFunctionArity}.cs`
- Test: `tests/Gridline.Core.Tests/Parsing/ParserTests.cs`

**Interfaces:**
- Produces (all `internal`):
  - `interface IFunctionArity { bool TryGetArity(string upperName, out int min, out int max); }`. A false return means the function is unknown, so it parses fine and later gives `#NAME?`.
  - AST: `abstract record Node`, with these subtypes:
    - `NumberNode(double Value, string Lexeme)`
    - `StringNode(string Value)`
    - `BoolNode(bool Value)`
    - `ErrorNode(string Code)`
    - `RefNode(int Row, int Col, bool RowAbs, bool ColAbs)`
    - `RangeNode(RefNode First, RefNode Second)`. Keep both corners **as written**; normalize at evaluation time.
    - `NameNode(string Name)`, with the name as written.
    - `UnaryNode(char Op, Node Operand)`
    - `PercentNode(Node Operand)`
    - `BinaryNode(string Op, Node Left, Node Right)`
    - `CallNode(string Name, IReadOnlyList<Node?> Args)`. `Name` is as written, and a null arg means omitted.
    - `ParenNode(Node Inner)`. Phase 2 printing depends on this node.
  - `sealed record ParsedFormula(Node? Root, bool IsParseError)`.
  - `static class Parser { static ParsedFormula Parse(string formulaBody, IFunctionArity arity); }`. It never throws. Recursion follows the AST depth, which §6.7 permits.

Arity rules:
- An empty argument list counts as 0 args: `PI()` has 0 args, and `F(,)` has 2 omitted args.
- Count omitted args toward arity.
- Outside `[min,max]` → `IsParseError`.
- `:` with anything but two `CellRef` tokens → parse error.

- [ ] **Step 1: Write failing tests.** Use a test `FakeArity` with `SUM 1..255`, `PI 0..0`, `IF 2..3` and `NOT 1..1`.

```csharp
[Theory]
[InlineData("1+")] [InlineData("(1")] [InlineData("1)")] [InlineData("")] [InlineData("SUM (A1)")] [InlineData("A1:1")] [InlineData("(A1):B2")] [InlineData("A1:B2:C3")] [InlineData("Rate:A1")]
[InlineData("PI(1)")] [InlineData("IF(1)")] [InlineData("NOT()")] [InlineData("1 2")]
public void ParseErrors(string s) => Assert.True(Parser.Parse(s, new FakeArity()).IsParseError);

[Fact] public void UnknownFunction_Parses() => Assert.False(Parser.Parse("FOO(1,2)", new FakeArity()).IsParseError);
[Fact] public void OmittedArgs() { var p = (CallNode)Parser.Parse("IF(A1,,3)", new FakeArity()).Root!; Assert.Null(p.Args[1]); Assert.Equal(3, p.Args.Count); }
[Fact] public void UnaryBindsTighterThanPower() { var b = (BinaryNode)Parser.Parse("-2^2", new FakeArity()).Root!; Assert.Equal("^", b.Op); Assert.IsType<UnaryNode>(b.Left); }
[Fact] public void PowerLeftAssoc() { var b = (BinaryNode)Parser.Parse("2^3^2", new FakeArity()).Root!; Assert.IsType<BinaryNode>(b.Left); }
[Fact] public void PercentTwice() => Assert.IsType<PercentNode>(((PercentNode)Parser.Parse("50%%", new FakeArity()).Root!).Operand);
[Fact] public void ParensRetained() => Assert.IsType<ParenNode>(Parser.Parse("(1)", new FakeArity()).Root);
[Fact] public void ConcatBelowAdditive() { var b = (BinaryNode)Parser.Parse("1+2&3", new FakeArity()).Root!; Assert.Equal("&", b.Op); }
[Fact] public void CompareBelowConcat() { var b = (BinaryNode)Parser.Parse("\"a\"&1=1", new FakeArity()).Root!; Assert.Equal("=", b.Op); }
[Fact] public void RefAbsFlags() { var r = (RefNode)Parser.Parse("$B7", new FakeArity()).Root!; Assert.True(r.ColAbs); Assert.False(r.RowAbs); }
[Fact] public void ArgCountMax255() { Assert.False(Parser.Parse("SUM(" + string.Join(",", Enumerable.Repeat("1", 255)) + ")", new FakeArity()).IsParseError); Assert.True(Parser.Parse("SUM(" + string.Join(",", Enumerable.Repeat("1", 256)) + ")", new FakeArity()).IsParseError); }
```

- [ ] **Step 2:** Run with `--filter ParserTests`. Expected: FAIL.
- [ ] **Step 3:** Implement precedence climbing exactly per the §5.3 grammar. Parse `NumberNode.Value` with `double.Parse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture)`. An overflowing literal such as `1e999` is a parse error; record that in DECISIONS.md.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): formula parser and AST (§5.2-5.4)`.

---

### Task 5: Coercion, comparison and the expression evaluator (§4.3 rule 5, §6.2–6.6)

**Files:**
- Create: `src/Gridline.Core/Evaluation/{NumberGrammar,Coercion,Comparison,IEvalContext,Arg,RangeRef,IFunction,Evaluator}.cs`
- Test: `tests/Gridline.Core.Tests/Evaluation/{CoercionTests,EvaluatorTests}.cs`

**Interfaces:**
- Consumes: Task 2 `CellValue`; Task 4 AST.
- Produces (all `internal`):
  - `static class NumberGrammar { static bool TryParse(string s, out double value); }`. It implements §4.3 rule 5, including `%`. It returns false for non-finite results. It does not trim.
  - `static class Coercion`:
    - `CellValue ToNumber(CellValue v)` returns Number or Error. Text is trimmed of U+0020 only, then passed to `NumberGrammar`.
    - `CellValue ToText(CellValue v)` uses G15 invariant for numbers.
    - `CellValue ToBoolean(CellValue v)`.
  - `static class Comparison { static int Compare(CellValue a, CellValue b); }`. Neither argument is an error. It applies the blank adaptation, then the Number<Text<Boolean ordering, ordinal uppercase-invariant text comparison, and exact doubles.
  - `interface IEvalContext`:
    - `CellValue GetCellValue(int row, int col)` returns `Blank` for empty cells.
    - `bool TryResolveName(string name, out RangeAddress target, out bool isRange)` returns false when the name is undefined.
    - `IEnumerable<(int Row, int Col, CellValue Value)> EnumerateNonBlank(RangeAddress range)` visits cells row-major, non-blank only, in O(non-blank cells in range + log n).
    - `int FormulaRow { get; }` and `int FormulaCol { get; }` give the cell being evaluated (used by Phase 3 ROW/COLUMN).
  - `sealed class RangeRef(RangeAddress Address, IEvalContext Ctx)`:
    - `int Rows`, `int Cols`.
    - `CellValue Get(int r, int c)`, 0-based.
    - `IEnumerable<CellValue> NonBlankRowMajor()`.
    - `int CellCount`.
  - `sealed class Arg`:
    - `bool IsOmitted`, `bool IsReference` (a ref, range or name, per §7.0).
    - `bool IsRange`, true for a range ref or a name bound to a range.
    - `RangeRef? Range`. This is non-null for range args and for single-cell reference args (as 1×1), so agg functions treat every reference uniformly.
    - `CellValue Evaluate()` gives the scalar value. A range in scalar position gives `#VALUE!`. A reference to a blank cell gives `Blank`, not 0. Evaluation is lazy and cached, at most once per call.
  - `interface IFunction { CellValue Invoke(IReadOnlyList<Arg> args, IEvalContext ctx); }`
  - `sealed class Evaluator(IReadOnlyDictionary<string, IFunction> functions)`, keyed by upper-case name. `CellValue Evaluate(ParsedFormula f, IEvalContext ctx)` gives the **final** value: a parse error → `#PARSE!`, a top-level Blank → Number 0, and text over 32,767 → `#VALUE!`.

**Decision to record (§6.3/§6.6):** internally, a reference to a blank cell yields `Blank`, so that comparison's "blank adapts" rule can see it (`=A1=""` with A1 blank → TRUE). Arithmetic coerces Blank→0 and concatenation coerces Blank→"". Only the formula's final result maps Blank→0.

Operators (§6.3):
- Binary operators evaluate the left operand, then the right. The first error wins.
- Arithmetic produces its result through `CellValue.FromNumber`, which turns non-finite values into `#NUM!`. `x/0` → `#DIV/0!`.
- `^`: `0^0` → `#NUM!`; `0^neg` → `#DIV/0!`; a negative base with a non-integer exponent → `#NUM!`.
- Unary `+` returns its operand unchanged.
- An undefined name → `#NAME?`. An unknown function → `#NAME?`. Do not evaluate its args.

- [ ] **Step 1: Write failing tests.** `EvaluatorTests` uses a small `DictContext : IEvalContext` test double and a helper `string Eval(string formula, params (string addr, CellValue v)[] cells)`. The helper parses the formula with an arity stub, evaluates it, and returns the canonical string.

```csharp
[Theory]
[InlineData("-2^2", "N:4")] [InlineData("2^3^2", "N:64")] [InlineData("1+2&3", "T:33")] [InlineData("\"a\"&1=1", "B:FALSE")] [InlineData("50%%", "N:0.005")]
[InlineData("1/0", "E:#DIV/0!")] [InlineData("0^0", "E:#NUM!")] [InlineData("0^-1", "E:#DIV/0!")] [InlineData("(-8)^(1/3)", "E:#NUM!")] [InlineData("10^400", "E:#NUM!")]
[InlineData("\"2024-01-01\"+0", "E:#VALUE!")] [InlineData("\" 5 \"+1", "N:6")] [InlineData("\"5%\"*1", "N:0.05")] [InlineData("\"\"+1", "E:#VALUE!")]
[InlineData("TRUE+1", "N:2")] [InlineData("1&TRUE", "T:1TRUE")] [InlineData("0.1+0.2&\"\"", "T:0.3")]
[InlineData("\"abc\"<\"ABD\"", "B:TRUE")] [InlineData("1<\"a\"", "B:TRUE")] [InlineData("\"a\"<TRUE", "B:TRUE")] [InlineData("\"A\"=\"a\"", "B:TRUE")]
[InlineData("#N/A+1", "E:#N/A")] [InlineData("#REF!+#N/A", "E:#REF!")] [InlineData("+\"x\"", "T:x")] [InlineData("-\"x\"", "E:#VALUE!")]
[InlineData("Undefined", "E:#NAME?")] [InlineData("FOO(1/0)", "E:#NAME?")]
public void Operators(string f, string expected) => Assert.Equal(expected, Eval(f));

[Fact] public void BlankRefScalarIsZero() => Assert.Equal("N:0", Eval("A1"));
[Fact] public void BlankComparesEqualToEmptyText() => Assert.Equal("B:TRUE", Eval("A1=\"\""));
[Fact] public void BlankComparesEqualToZeroAndFalse() { Assert.Equal("B:TRUE", Eval("A1=0")); Assert.Equal("B:TRUE", Eval("A1=FALSE")); Assert.Equal("B:TRUE", Eval("A1=B1")); }
[Fact] public void BlankConcat() => Assert.Equal("T:x", Eval("A1&\"x\""));
[Fact] public void RangeInScalarContext() { Assert.Equal("E:#VALUE!", Eval("A1:B2")); Assert.Equal("E:#VALUE!", Eval("A1:B2+1")); }
[Fact] public void TextLimit() => Assert.Equal("E:#VALUE!", Eval("A1&A1", ("A1", CellValue.FromText(new string('x', 20000)))));
[Fact] public void ParseErrorFormula() => Assert.Equal("E:#PARSE!", Eval("1+"));
```

In `CoercionTests`, add a theory over `NumberGrammar.TryParse` with these cases:
- Accepted: `"1"`, `"-1.5e3"`, `".5"`, `"5."`, `"+3%"`, `"1E+2"`.
- Rejected: `"1,000"`, `" 5"`, `"$5"`, `"1e"`, `"."`, `"1e999"` (non-finite), `"--1"`, `""`.

- [ ] **Step 2:** Run with `--filter "CoercionTests|EvaluatorTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement. `NumberGrammar` is a hand-written scanner over the exact regex, followed by `double.Parse` invariant on the part without `%`.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): coercion, comparison, evaluator (§6.2-6.6)`.

---

### Task 6: Function registry and Math functions (§7.0, §7.1, §6.9)

**Files:**
- Create: `src/Gridline.Core/Functions/{FunctionRegistry,Aggregation,MathFunctions,Rounding}.cs`, `src/Gridline.Core/Evaluation/FunctionArityAdapter.cs`
- Test: `tests/Gridline.Core.Tests/Functions/MathFunctionTests.cs`

**Interfaces:**
- Consumes: Task 5 `Arg`, `IFunction`, `Coercion`, `Evaluator`.
- Produces:
  - `internal sealed class FunctionRegistry`:
    - `static FunctionRegistry CreateDefault()`
    - `IReadOnlyDictionary<string, IFunction> Functions`
    - `bool TryGetArity(string upper, out int min, out int max)`
    - `void Register(string name, int min, int max, IFunction f)`, used only during construction.
    - Later tasks add their functions by extending `CreateDefault` through a static `Register(FunctionRegistry r)` method on each `*Functions` class.
  - `internal sealed class FunctionArityAdapter(FunctionRegistry r) : IFunctionArity`.
  - `internal static class Aggregation { static CellValue Collect(IReadOnlyList<Arg> args, List<double> into); }`. It applies the §7.0 agg table and returns the first error, or `default` (Blank) on success.
  - `internal static class Coercion` gains `static bool TryToInt(CellValue v, out int value, out CellValue error)`, which truncates toward zero (§6.9).
  - `internal static class Rounding { static double Round(double n, int digits, RoundMode mode); }` with `enum RoundMode { HalfAwayFromZero, Up, Down }`.
  - Add a test helper `CoreTestHost.Eval(string formula, params (string, string)[] inputs)` in `tests/Gridline.Core.Tests/TestHost.cs`. Until Task 10 it builds a `DictContext`; after Task 10 it switches to a real `Workbook`. It returns the canonical value string.

Arities:
- SUM, AVERAGE, MIN, MAX, PRODUCT, COUNT, COUNTA, SUMSQ: 1–255.
- SUMPRODUCT: 1–255.
- COUNTBLANK: 1.
- ABS, SIGN, INT, SQRT: 1.
- MOD, POWER: 2.
- PI: 0.
- ROUND, ROUNDUP, ROUNDDOWN: 2.

COUNTBLANK needs a reference argument. A direct argument gives `#VALUE!`; record this in DECISIONS.md.

**Rounding algorithm:**
1. Take `s = n.ToString("R", Invariant)`.
2. Split `s` into sign, digit string D and decimal exponent E, so that |n| = 0.D × 10^E.
3. The cut position is `E + digits`. If the cut is at or past `len(D)`, return n unchanged. If the cut is below 0, return 0.
4. Otherwise keep `D[0..cut]` and decide the increment:
   - HalfAwayFromZero: the next digit is ≥ 5.
   - Up: any dropped digit is non-zero.
   - Down: never.
5. Propagate the carry, rebuild the string as `"0." + digits + "E" + exp`, and `double.Parse` it.

- [ ] **Step 1: Write failing tests.** Use `CoreTestHost.Eval`. Inputs are cell inputs such as `("A1","5")`, `("A2","'7")` and `("A3","TRUE")`.

```csharp
[Theory]
[InlineData("ROUND(2.675,2)", "N:2.68")] [InlineData("ROUND(0.285,2)", "N:0.29")] [InlineData("ROUND(-2.5,0)", "N:-3")] [InlineData("ROUND(1250,-2)", "N:1300")]
[InlineData("ROUNDUP(1.201,2)", "N:1.21")] [InlineData("ROUNDUP(-1.201,2)", "N:-1.21")] [InlineData("ROUNDDOWN(-1.209,2)", "N:-1.2")] [InlineData("ROUND(5,-1)", "N:10")] [InlineData("ROUND(4,-1)", "N:0")] [InlineData("ROUND(1.5,1.9)", "N:1.5")] [InlineData("ROUND(123,-5)", "N:0")]
[InlineData("INT(-2.5)", "N:-3")] [InlineData("MOD(-3,2)", "N:1")] [InlineData("MOD(3,-2)", "N:-1")] [InlineData("MOD(1,0)", "E:#DIV/0!")]
[InlineData("SQRT(-1)", "E:#NUM!")] [InlineData("SIGN(-0.1)", "N:-1")] [InlineData("POWER(0,0)", "E:#NUM!")] [InlineData("ABS(-3)", "N:3")] [InlineData("PI()", "N:3.141592653589793")]
[InlineData("SUM()", "E:#PARSE!")] [InlineData("SUM(1,\"2\",TRUE)", "N:4")] [InlineData("SUM(\"x\")", "E:#VALUE!")] [InlineData("AVERAGE(\"x\"&\"\")", "E:#VALUE!")]
public void Direct(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f));

[Fact] public void Agg_ReferenceIgnoresTextBoolBlank() => Assert.Equal("N:5", CoreTestHost.Eval("=SUM(A1:A4)", ("A1","5"), ("A2","'7"), ("A3","TRUE")));
[Fact] public void Agg_ReferenceErrorPropagates() => Assert.Equal("E:#DIV/0!", CoreTestHost.Eval("=SUM(A1:A2)", ("A1","5"), ("A2","=1/0")));
[Fact] public void Average_NoNumbers() => Assert.Equal("E:#DIV/0!", CoreTestHost.Eval("=AVERAGE(A1:A3)"));
[Fact] public void MinMaxProduct_NoNumbers_Zero() { Assert.Equal("N:0", CoreTestHost.Eval("=MIN(A1:A3)")); Assert.Equal("N:0", CoreTestHost.Eval("=MAX(A1)")); Assert.Equal("N:0", CoreTestHost.Eval("=PRODUCT(A1:A2)")); }
[Fact] public void Count_RefIgnoresErrors_DirectCountsBoolAndNumericText() { Assert.Equal("N:1", CoreTestHost.Eval("=COUNT(A1:A3)", ("A1","1"), ("A2","=1/0"), ("A3","'2"))); Assert.Equal("N:3", CoreTestHost.Eval("=COUNT(1,TRUE,\"3\",\"x\",1/0)")); }
[Fact] public void CountA_IncludesErrorsAndEmptyText() => Assert.Equal("N:3", CoreTestHost.Eval("=COUNTA(A1:A4)", ("A1","1"), ("A2","=1/0"), ("A3","=\"\"")));
[Fact] public void CountBlank_CountsEmptyTextFormulas() => Assert.Equal("N:3", CoreTestHost.Eval("=COUNTBLANK(A1:A4)", ("A1","1"), ("A2","=\"\"")));
[Fact] public void SumProduct() { Assert.Equal("N:11", CoreTestHost.Eval("=SUMPRODUCT(A1:A2,B1:B2)", ("A1","1"),("A2","2"),("B1","3"),("B2","4"))); Assert.Equal("E:#VALUE!", CoreTestHost.Eval("=SUMPRODUCT(A1:A2,B1:B3)")); Assert.Equal("N:0", CoreTestHost.Eval("=SUMPRODUCT(A1:A2,B1:B2)", ("A1","'x"),("B1","3"))); }
[Fact] public void SumSq() => Assert.Equal("N:25", CoreTestHost.Eval("=SUMSQ(3,4)"));
[Fact] public void FunctionNamesCaseInsensitive() => Assert.Equal("N:3", CoreTestHost.Eval("=sUm(1,2)"));
```

- [ ] **Step 2:** Run with `--filter MathFunctionTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the 20 functions. Each function is a small private class or a lambda wrapped in an `IFunction` adapter (`DelegateFunction`).
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): math functions (§7.1)`.

---

### Task 7: Logic and information functions (§7.2)

**Files:**
- Create: `src/Gridline.Core/Functions/LogicFunctions.cs`
- Test: `tests/Gridline.Core.Tests/Functions/LogicFunctionTests.cs`

**Interfaces:**
- Consumes: Task 6 registry pattern and `Arg` laziness.
- Arities:
  - IF: 2–3.
  - AND, OR, XOR: 1–255.
  - NOT: 1.
  - IFERROR, IFNA: 2.
  - ISBLANK, ISNUMBER, ISTEXT, ISLOGICAL, ISERROR, ISNA: 1.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData("IF(TRUE,1,1/0)", "N:1")] [InlineData("IF(FALSE,1/0,2)", "N:2")] [InlineData("IF(FALSE,1)", "B:FALSE")] [InlineData("IF(TRUE,,1)", "N:0")] [InlineData("IF(\"x\",1,2)", "E:#VALUE!")] [InlineData("IF(\"true\",1,2)", "N:1")]
[InlineData("AND(TRUE,1)", "B:TRUE")] [InlineData("OR(0,FALSE)", "B:FALSE")] [InlineData("XOR(TRUE,TRUE,TRUE)", "B:TRUE")] [InlineData("AND(\"x\")", "E:#VALUE!")]
[InlineData("NOT(0)", "B:TRUE")] [InlineData("IFERROR(1/0,7)", "N:7")] [InlineData("IFERROR(1,1/0)", "N:1")] [InlineData("IFNA(#N/A,1)", "N:1")] [InlineData("IFNA(#DIV/0!,1)", "E:#DIV/0!")]
[InlineData("ISNUMBER(\"1\")", "B:FALSE")] [InlineData("ISTEXT(\"\")", "B:TRUE")] [InlineData("ISLOGICAL(1)", "B:FALSE")] [InlineData("ISERROR(#N/A)", "B:TRUE")] [InlineData("ISNA(#REF!)", "B:FALSE")] [InlineData("ISBLANK(\"\")", "B:FALSE")]
public void Direct(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f));

[Fact] public void AndOr_RefIgnoresTextAndBlank() { Assert.Equal("B:TRUE", CoreTestHost.Eval("=AND(A1:A3)", ("A1","TRUE"), ("A2","'x"))); Assert.Equal("E:#VALUE!", CoreTestHost.Eval("=OR(A1:A2)", ("A1","'x"))); Assert.Equal("B:TRUE", CoreTestHost.Eval("=OR(A1)", ("A1","2"))); }
[Fact] public void IsBlank_TrueOnlyForTrulyBlankRef() { Assert.Equal("B:TRUE", CoreTestHost.Eval("=ISBLANK(A1)")); Assert.Equal("B:FALSE", CoreTestHost.Eval("=ISBLANK(A1)", ("A1","=\"\""))); }
[Fact] public void IsFunctions_NeverError() => Assert.Equal("B:FALSE", CoreTestHost.Eval("=ISNUMBER(A1:A2)"));
```

`ISNUMBER(A1:A2)` evaluates its range argument in scalar context, which gives `#VALUE!`, and ISNUMBER of that error is FALSE.

- [ ] **Step 2:** Run with `--filter LogicFunctionTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the 13 functions.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): logic and info functions (§7.2)`.

---

### Task 8: Text functions (§7.3)

**Files:**
- Create: `src/Gridline.Core/Functions/{TextFunctions,Wildcard}.cs`
- Test: `tests/Gridline.Core.Tests/Functions/TextFunctionTests.cs`

**Interfaces:**
- Produces: `internal static class Wildcard`:
  - `static bool IsMatch(string pattern, string text)` matches the whole string, case-insensitive (uppercase invariant), with `* ? ~`.
  - `static int Search(string pattern, string text, int startIndex0)` returns the 0-based index of the first match **starting at** each position ≥ start, matching a prefix of the remaining text. It returns -1 when not found.
  - Implement both with an iterative two-pointer backtracking matcher (no regex, no recursion). Phase 3 reuses `IsMatch`.
- Arities:
  - LEN, UPPER, LOWER, PROPER, TRIM, VALUE: 1.
  - LEFT, RIGHT: 1–2.
  - MID: 3.
  - CONCAT: 1–255.
  - TEXTJOIN: 3–255.
  - FIND, SEARCH: 2–3.
  - SUBSTITUTE: 3–4.
  - REPLACE: 4.
  - REPT: 2.
  - EXACT: 2.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData("LEN(\"abc\")", "N:3")] [InlineData("LEFT(\"abc\")", "T:a")] [InlineData("LEFT(\"abc\",9)", "T:abc")] [InlineData("RIGHT(\"abc\",-1)", "E:#VALUE!")]
[InlineData("MID(\"abc\",4,1)", "T:")] [InlineData("MID(\"abc\",0,1)", "E:#VALUE!")] [InlineData("MID(\"abcdef\",2,3)", "T:bcd")]
[InlineData("PROPER(\"o'neil 2nd\")", "T:O'Neil 2Nd")] [InlineData("TRIM(\"  a   b  \")", "T:a b")] [InlineData("UPPER(\"aé\")", "T:AÉ")]
[InlineData("CONCAT(\"a\",1,TRUE)", "T:a1TRUE")] [InlineData("FIND(\"\",\"abc\",2)", "N:2")] [InlineData("FIND(\"B\",\"abc\")", "E:#VALUE!")] [InlineData("FIND(\"a\",\"abc\",5)", "E:#VALUE!")] [InlineData("FIND(\"c\",\"abc\",4)", "E:#VALUE!")] [InlineData("FIND(\"\",\"abc\",4)", "N:4")]
[InlineData("SEARCH(\"B\",\"abc\")", "N:2")] [InlineData("SEARCH(\"a?c\",\"xabc\")", "N:2")] [InlineData("SEARCH(\"b*d\",\"abcd\")", "N:2")] [InlineData("SEARCH(\"~*\",\"a*b\")", "N:2")]
[InlineData("SUBSTITUTE(\"aaa\",\"a\",\"b\",2)", "T:aba")] [InlineData("SUBSTITUTE(\"aaaa\",\"aa\",\"b\")", "T:bb")] [InlineData("SUBSTITUTE(\"abc\",\"\",\"x\")", "T:abc")] [InlineData("SUBSTITUTE(\"a\",\"a\",\"b\",0)", "E:#VALUE!")]
[InlineData("REPLACE(\"abc\",2,1,\"X\")", "T:aXc")] [InlineData("REPLACE(\"abc\",9,1,\"X\")", "T:abcX")] [InlineData("REPT(\"ab\",3)", "T:ababab")] [InlineData("REPT(\"a\",-1)", "E:#VALUE!")] [InlineData("REPT(\"a\",40000)", "E:#VALUE!")]
[InlineData("EXACT(\"a\",\"A\")", "B:FALSE")] [InlineData("VALUE(\" 1e3 \")", "N:1000")] [InlineData("VALUE(\"x\")", "E:#VALUE!")] [InlineData("LEN(1/3)", "N:17")]
public void Direct(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f));

[Fact] public void Concat_RangeRowMajorBlanksEmpty() => Assert.Equal("T:abc", CoreTestHost.Eval("=CONCAT(A1:B2)", ("A1","a"), ("B1","b"), ("B2","c")));
[Fact] public void TextJoin_IgnoreEmpty() { Assert.Equal("T:a,c", CoreTestHost.Eval("=TEXTJOIN(\",\",TRUE,A1:A3)", ("A1","a"), ("A3","c"))); Assert.Equal("T:a,,c", CoreTestHost.Eval("=TEXTJOIN(\",\",FALSE,A1:A3)", ("A1","a"), ("A3","c"))); }
```

`LEN(1/3)` is 17 because G15 gives `0.333333333333333`.

- [ ] **Step 2:** Run with `--filter TextFunctionTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the 17 functions. Every text result is checked against the 32,767 limit, which gives `#VALUE!`.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): text functions (§7.3)`.

---

### Task 9: Dependency graph and recalc planner (§6.1, §6.7)

**Files:**
- Create: `src/Gridline.Core/Dependencies/{DependencyGraph,RecalcPlanner}.cs`
- Test: `tests/Gridline.Core.Tests/Dependencies/DependencyGraphTests.cs`

**Interfaces:**
- Consumes: Task 1 `CellAddress.Key` and `RangeAddress`. This namespace MUST NOT reference `Functions` or `Commands`.
- Produces (all `internal`):
  - `sealed record FormulaDeps(IReadOnlyList<int> Cells, IReadOnlyList<RangeAddress> Ranges, IReadOnlyList<string> Names)`. Names are stored upper-cased.
  - `sealed class DependencyGraph`:
    - `void SetDependencies(int cellKey, FormulaDeps? deps)` replaces the cell's dependencies; null removes them.
    - `void SetNameTarget(string upperName, RangeAddress? target)`. A name's dependents depend on the target's cells transitively. Model the name as a virtual node with id `-(index+1)`.
    - `IEnumerable<int> DirectDependents(int key)` yields formula cells that reference this cell directly, through a range, or through a name whose target contains it.
    - `IEnumerable<int> NameDependents(string upperName)`.
  - `sealed class RecalcPlanner(DependencyGraph g)`:
    - `RecalcPlan Plan(IEnumerable<int> changedKeys, IEnumerable<string> changedNames, Func<int,bool> isFormula)`
    - `RecalcPlan PlanAll(IEnumerable<int> allFormulaKeys)`
  - `sealed record RecalcPlan(IReadOnlyList<int> Order, IReadOnlySet<int> CycleMembers)`. `Order` holds every affected formula cell in topological order, with cycle members included where their SCC falls, and it is evaluated exactly once each.

**Algorithm (MUST be iterative; §6.7):**
1. Run an explicit-stack DFS over `DirectDependents`, starting from the changed keys and the dependents of the changed names. It collects the affected set A: the changed cells that are formulas, plus all transitive dependents.
2. Run an iterative Tarjan SCC over the subgraph induced on A. Edges go from a precedent to its dependent.
   - An SCC of size > 1, or a self-loop, puts its members in the cycle set.
   - Tarjan emits SCCs in reverse topological order, so reverse that list to get `Order`.
3. Range lookup is an index from column to a list of distinct ranges covering it, each with its dependent set. A changed cell checks only the ranges in its column. Dedupe identical ranges, so that 1,000 copies of `$A$1:$A$100000` cost one entry.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Chain_TopologicalOrder() { /* B1 deps A1, C1 deps B1; Plan([A1]) → Order == [B1, C1] */ }
[Fact] public void RangeDependents_Found() { /* D1 deps range A1:A10; Plan([A5]) contains D1; Plan([B5]) empty */ }
[Fact] public void SelfReference_IsCycle() { /* A1 deps A1 → CycleMembers == {A1} */ }
[Fact] public void Cycle_DependentsNotMembers() { /* A1<->B1, C1 deps A1 → CycleMembers {A1,B1}; Order lists C1 after A1,B1 */ }
[Fact] public void NameDependents() { /* SetNameTarget("RATE", B1); C1 deps name RATE; Plan([B1]) contains C1; Plan([], ["RATE"]) contains C1 */ }
[Fact] public void Recalc_LongChain_NoStackOverflow() { /* 100_000-cell chain; Plan([A1]).Order.Count == 99_999 */ }
[Fact] public void Recalc_RangeChain_NoStackOverflow() { /* A{n} deps range A1:A{n-1}, n=2..20000; Plan([A1]).Order.Count == 19_999, finishes < 5 s */ }
```

Write these tests out in full, with concrete keys built through `CellAddress.Parse(...).Key`. The comments above state each test's exact assertions.

- [ ] **Step 2:** Run with `--filter DependencyGraphTests`. Expected: FAIL.
- [ ] **Step 3:** Implement. Record the column-bucket range index in DECISIONS.md (§6.7, §12).
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): dependency graph and iterative SCC recalc (§6.1, §6.7)`.

---

### Task 10: Workbook Phase 1 API (§4.3, §9.1, §9.2)

**Files:**
- Create: `src/Gridline.Core/{Workbook,InputInterpreter,CellStore,Cell}.cs`
- Modify: `tests/Gridline.Core.Tests/TestHost.cs` so it uses `Workbook`.
- Test: `tests/Gridline.Core.Tests/WorkbookTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–9.
- Produces:
  - The §9.1 Phase 1 members, exactly as written.
  - `public static Workbook FromSnapshot(IReadOnlyList<KeyValuePair<string,string>> cells, IReadOnlyList<KeyValuePair<string,string>> names)`. It throws `ArgumentException` on an invalid or duplicate address, an input over the limit, or a bad name. After the call, a full recalc has run, `LastRecalc` equals the formula count, undo history is empty, the workbook is not dirty, and no event has been raised. The IO loader uses this method. Phase 1 accepts only an empty `names`; Phase 3 implements names.
  - `internal sealed class CellStore`:
    - `bool TryGet(int key, out Cell c)`, `void Set(int key, Cell c)`, `void Remove(int key)`
    - `IEnumerable<int> KeysRowMajor()`
    - `IEnumerable<int> KeysInRange(RangeAddress r)`, which is row-major and touches only occupied cells.
    - The design is a `Dictionary<int,Cell>` plus per-column sorted `List<int>` rows, using binary-search insert. Iterating a range merges the column lists by row.
  - `internal sealed class Cell { string Input; CellKind Kind; ParsedFormula? Formula; CellValue Value; FormulaDeps? Deps; }`
  - `internal static class InputInterpreter { static Cell Interpret(string input, Parser/arity) }`, implementing §4.3 rules 1–6.
  - The Workbook implements `IEvalContext` through a private nested class. Phase 3 adds names to it.

`CellsChanged` behaviour (§9.2):
- Collect the addresses whose input changed, plus those whose `Value` changed. Compare with `CellValue.Equals`.
- Raise the event once per call, after recalc, and only when that set is non-empty.
- Addresses are canonical and in row-major order.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData("5", "N:5")] [InlineData(" 5", "T: 5")] [InlineData("'=1", "T:=1")] [InlineData("true", "B:TRUE")] [InlineData("50%", "N:0.5")] [InlineData("1e999", "T:1e999")] [InlineData("1,000", "T:1,000")] [InlineData("=", "E:#PARSE!")] [InlineData("-0", "N:0")]
public void InputInterpretation(string input, string value) { var wb = new Workbook(); wb.SetCell("A1", input); Assert.Equal(value, wb.GetValue("A1").ToCanonicalString()); Assert.Equal(input, wb.GetInput("a1")); }

[Fact] public void EmptyInput_RemovesCell() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A1",""); Assert.Empty(wb.GetUsedAddresses()); Assert.Equal(ValueKind.Blank, wb.GetValue("A1").Kind); }
[Fact] public void UsedAddresses_RowMajor() { var wb = new Workbook(); wb.SetCell("B2","1"); wb.SetCell("ZZ1","1"); wb.SetCell("A2","1"); Assert.Equal(new[]{"ZZ1","A2","B2"}, wb.GetUsedAddresses()); }
[Fact] public void Recalc_Incremental() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("B1","=A1+1"); wb.SetCell("C1","=B1*2"); wb.SetCell("D1","=5"); wb.SetCell("A1","10"); Assert.Equal("N:22", wb.GetValue("C1").ToCanonicalString()); Assert.Equal(2, wb.LastRecalc.EvaluatedFormulaCount); wb.SetCell("Z9","3"); Assert.Equal(0, wb.LastRecalc.EvaluatedFormulaCount); }
[Fact] public void BlankInRangeTriggersDependents() { var wb = new Workbook(); wb.SetCell("B1","=SUM(A1:A3)"); wb.SetCell("A2","4"); Assert.Equal("N:4", wb.GetValue("B1").ToCanonicalString()); }
[Fact] public void Cycle_AllMembersCirc_DependentsGetErrorValue() { var wb = new Workbook(); wb.SetCell("A1","=B1"); wb.SetCell("B1","=A1"); wb.SetCell("C1","=IFERROR(A1,0)"); Assert.Equal("E:#CIRC!", wb.GetValue("A1").ToCanonicalString()); Assert.Equal("E:#CIRC!", wb.GetValue("B1").ToCanonicalString()); Assert.Equal("N:0", wb.GetValue("C1").ToCanonicalString()); }
[Fact] public void Cycle_UntakenBranchStillCycle() { var wb = new Workbook(); wb.SetCell("A1","=IF(TRUE,1,A1)"); Assert.Equal("E:#CIRC!", wb.GetValue("A1").ToCanonicalString()); }
[Fact] public void Workbook_BreakingCycle_RestoresAllMembersAndDependents() { var wb = new Workbook(); wb.SetCell("A1","=B1"); wb.SetCell("B1","=A1"); wb.SetCell("C1","=A1+1"); wb.SetCell("B1","5"); Assert.Equal("N:5", wb.GetValue("A1").ToCanonicalString()); Assert.Equal("N:6", wb.GetValue("C1").ToCanonicalString()); }
[Fact] public void Workbook_InvalidAddress_ThrowsAndChangesNothing() { var wb = new Workbook(); wb.SetCell("A1","1"); int events = 0; wb.CellsChanged += (_, _) => events++; Assert.Throws<ArgumentException>(() => wb.SetCell("ZZZ1","x")); Assert.Throws<ArgumentException>(() => wb.GetValue("A0")); Assert.Throws<ArgumentException>(() => wb.GetInput("")); Assert.Throws<ArgumentException>(() => wb.SetCell("A1", new string('x', 32768))); Assert.Equal(0, events); Assert.Equal("1", wb.GetInput("A1")); }
[Fact] public void CellsChanged_OncePerCall_IncludesDependents() { var wb = new Workbook(); wb.SetCell("B1","=A1"); IReadOnlyCollection<string>? got = null; int n = 0; wb.CellsChanged += (_, e) => { n++; got = e.Addresses; }; wb.SetCell("A1","2"); Assert.Equal(1, n); Assert.Equal(new[]{"A1","B1"}, got); n = 0; wb.SetCell("A1","2"); Assert.Equal(0, n); }
[Fact] public void Eval_WordTokenEdgeCases() { var wb = new Workbook(); wb.SetCell("A1","=A0"); wb.SetCell("A2","=$A0"); wb.SetCell("A3","=SUM (B1)"); wb.SetCell("A4","=sum(b1)"); Assert.Equal("E:#NAME?", wb.GetValue("A1").ToCanonicalString()); Assert.Equal("E:#PARSE!", wb.GetValue("A2").ToCanonicalString()); Assert.Equal("E:#PARSE!", wb.GetValue("A3").ToCanonicalString()); Assert.Equal("N:0", wb.GetValue("A4").ToCanonicalString()); }
[Fact] public void FromSnapshot_FullRecalc() { var wb = Workbook.FromSnapshot(new[]{ KeyValuePair.Create("B1","=A1*2"), KeyValuePair.Create("A1","3") }, []); Assert.Equal("N:6", wb.GetValue("B1").ToCanonicalString()); Assert.Equal(1, wb.LastRecalc.EvaluatedFormulaCount); }
[Fact] public void FromSnapshot_DuplicateCaseInsensitive_Throws() => Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot(new[]{ KeyValuePair.Create("a1","1"), KeyValuePair.Create("A1","2") }, []));
```

- [ ] **Step 2:** Run with `--filter WorkbookTests`. Expected: FAIL.
- [ ] **Step 3:** Implement. On each `SetCell`:
  1. Validate everything first, so the call is atomic.
  2. Interpret the input.
  3. Update the store and the graph.
  4. Plan and evaluate the cells in order, giving cycle members `#CIRC!`.
  5. Set `LastRecalc`.
  6. Raise the event.

  A changed constant cell is not "evaluated", so it does not count toward `EvaluatedFormulaCount`.
- [ ] **Step 4:** Run `dotnet test tests/Gridline.Core.Tests -c Release`. Expected: all tests PASS, including Tasks 6–8 now running through the real `Workbook`. Commit: `feat(core): Workbook phase 1 API (§4.3, §9.1, §9.2)`.

---

### Task 11: `.gridline` format and atomic save (§8.1, §8.2, §8.4)

**Files:**
- Create: `src/Gridline.IO/{GridlineFile,GridlineFormatException,JsonEscaper}.cs`
- Test: `tests/Gridline.IO.Tests/GridlineFileTests.cs`, `tests/Gridline.IO.Tests/RoundTripProperties.cs`

**Interfaces:**
- Consumes: `Workbook.FromSnapshot`, `GetUsedAddresses`, `GetInput`, `GetNames` (an empty list in Phase 1; add an `internal`-free public `GetNames` stub returning empty now, and Phase 3 fills it).
- Produces (`public`, namespace `Gridline.IO`):
  - `sealed class GridlineFormatException : Exception`, with the `(string message)` and `(string message, Exception inner)` constructors.
  - `static class GridlineFile`:
    - `string Serialize(Workbook wb)` gives the exact text with LF line endings and a trailing `\n`.
    - `byte[] SerializeToUtf8(Workbook wb)` gives the same text as UTF-8 without a BOM.
    - `Workbook Load(ReadOnlySpan<byte> utf8Json)` throws `GridlineFormatException`.
    - `Workbook LoadFromPath(string path)` lets `IOException`/`FileNotFoundException` propagate.
    - `void SaveToPath(Workbook wb, string path)` follows §8.4: temp file `path + "." + Guid + ".tmp"` in the same directory, `FileStream.Flush(true)`, then `File.Move(tmp, path, true)`.

Exact layout. An empty workbook gives:
```
{\n  "format": "gridline",\n  "version": 1,\n  "names": {},\n  "cells": []\n}\n
```
A non-empty workbook writes `"cells": [\n    ["A1", "..."],\n    ["B1", "..."]\n  ]`. Each entry is indented 4 spaces, entries are separated by commas, and there is no trailing comma. Names are written in the same style: `"names": {\n    "Rate": "$B$1"\n  },`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Save_Empty_ExactBytes() => Assert.Equal("{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {},\n  \"cells\": []\n}\n", GridlineFile.Serialize(new Workbook()));
[Fact] public void Save_Cells_ExactBytes() { var wb = new Workbook(); wb.SetCell("B1","5"); wb.SetCell("A1","=SUM(B1:B3)"); wb.SetCell("A2","hello \"world\""); Assert.Equal("{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {},\n  \"cells\": [\n    [\"A1\", \"=SUM(B1:B3)\"],\n    [\"B1\", \"5\"],\n    [\"A2\", \"hello \\\"world\\\"\"]\n  ]\n}\n", GridlineFile.Serialize(wb)); }
[Fact] public void Save_EscapesControlCharsExactly() { var wb = new Workbook(); wb.SetCell("A1", "a\\b\b\f\n\r\t\u0001é<>&'+😀"); Assert.Contains("[\"A1\", \"a\\\\b\\b\\f\\n\\r\\t\\u0001é<>&'+😀\"]", GridlineFile.Serialize(wb)); }
[Fact] public void Load_ThenSave_ByteIdentical() { var bytes = GridlineFile.SerializeToUtf8(Sample()); Assert.Equal(bytes, GridlineFile.SerializeToUtf8(GridlineFile.Load(bytes))); }
[Fact] public void Load_AcceptsAnyWhitespaceOrderEscapes() { var wb = GridlineFile.Load("{\"cells\":[[\"a1\",\"\\u0031\"]],\"version\":1,\"format\":\"gridline\",\"extra\":0}"u8); Assert.Equal("1", wb.GetInput("A1")); }
[Theory]
[InlineData("{")] [InlineData("{\"format\":\"x\",\"version\":1,\"cells\":[]}")] [InlineData("{\"format\":\"gridline\",\"cells\":[]}")] [InlineData("{\"format\":\"gridline\",\"version\":1.5,\"cells\":[]}")]
[InlineData("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\"]]}")] [InlineData("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\",5]]}")] [InlineData("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A0\",\"5\"]]}")]
[InlineData("{\"format\":\"gridline\",\"version\":1,\"cells\":[[\"A1\",\"5\"],[\"a1\",\"6\"]]}")]
public void Load_Invalid_Throws(string json) => Assert.Throws<GridlineFormatException>(() => GridlineFile.Load(Encoding.UTF8.GetBytes(json)));
[Fact] public void Load_FutureVersion_Message() => Assert.Contains("unsupported version", Assert.Throws<GridlineFormatException>(() => GridlineFile.Load("{\"format\":\"gridline\",\"version\":2,\"cells\":[]}"u8)).Message);
[Fact] public void Load_OverlongInput_Throws() { /* 32768-char input → GridlineFormatException */ }
[Fact] public void SaveToPath_ReplacesAtomically() { /* save twice to a temp dir path; final content == second serialization; no exception when target exists */ }
```

- Treat a missing `cells` or `names` key as empty. Record that in DECISIONS.md.
- In `RoundTripProperties`, add `[Property] Save_Load_Save_IsIdentity`. FsCheck generates up to 50 (address, input) pairs, with inputs drawn from arbitrary strings of ≤ 200 chars, including control chars and lone surrogates filtered to valid UTF-16. The serialized bytes after load must equal the original serialized bytes.
  - **Decision:** a lone surrogate cannot be encoded to UTF-8. `SetCell` accepts it, and `Serialize` writes it as `\udxxx`. Record that in DECISIONS.md.

- [ ] **Step 2:** Run `dotnet test tests/Gridline.IO.Tests -c Release`. Expected: FAIL.
- [ ] **Step 3:** Implement. The writer is a `StringBuilder` with `JsonEscaper.Append(sb, s)`. Load uses `JsonDocument.Parse` (wrap `JsonException` into `GridlineFormatException`), then validation, then `Workbook.FromSnapshot` (wrap `ArgumentException`).
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(io): gridline file format and atomic save (§8.1-8.4)`.

---

### Task 12: CLI Phase 1 (§10)

**Files:**
- Create: `src/Gridline.Cli/{Program,CliApp,ScriptRunner,Escaping}.cs`
- Test: `tests/Gridline.Cli.Tests/{CliAppTests,ScriptTests,ProcessTests}.cs`

**Interfaces:**
- Consumes: `Workbook`, `GridlineFile`, `GridlineFormatException`.
- Produces:
  - `public static class CliApp { public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr); }`. It writes `\n` explicitly and never calls `WriteLine`.
  - `Program.Main` wraps stdout and stderr in `new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = false }`, and reads stdin as UTF-8. It flushes on exit.
  - `internal static class Escaping`:
    - `string Escape(string s)` follows §8.3 (`\`, tab, LF, CR).
    - `string Unescape(string s)` handles SETX; an unknown escape is left verbatim (record in DECISIONS.md).
  - `ScriptRunner` keeps `bool batchOpen` and a pending list for Phase 2. Phase 1 prints `ERR\tnot implemented` for the UNDO, REDO, BEGIN, END, PASTE, DIRTY, INSERT*/DELETE*, NAME, UNNAME and NAMES commands. Unknown commands give `ERR\tunknown command: X`.

Exit code mapping:
- Usage → 1.
- `FileNotFoundException`, `DirectoryNotFoundException`, `IOException` or `UnauthorizedAccessException` → 2.
- `GridlineFormatException` → 3.
- An `ERR` line in a script → 4.
- An invalid address in `eval` or `set` → 5. Validate every address before printing anything.

Errors go to stderr as a single line, `error: <message>`, with newlines in the message replaced by spaces.

Script parsing: the command word is the text up to the first space. For SET/SETX, the address runs to the second space, and the input is everything after it, verbatim. `SET A1` with no second space gives `""`. Strip a trailing `\r` from each line. Blank lines and lines starting with `#` are skipped.

- [ ] **Step 1: Write failing tests.** Use a helper `(int code, string stdout, string stderr) Run(string stdin, params string[] args)`.

```csharp
[Fact] public void Script_SetGetDumpStats() { var r = Run("SET A1 2\nSET B1 =A1*3\n# c\n\nGET B1\nDUMP\nSTATS\n", "script"); Assert.Equal(0, r.code); Assert.Equal("B1\tN:6\nA1\tN:2\nB1\tN:6\nEVALUATED\t1\n", r.stdout); }
[Fact] public void Script_SetInputIsVerbatimAfterSecondSpace() { var r = Run("SET A1  x \nINPUT A1\nset a2\nGET A2\n", "script"); Assert.Equal("A1\t x \nA2\tBLANK\n", r.stdout); }
[Fact] public void Script_SetxInput_RoundTripsEscapes() { var r = Run("SETX A1 a\\tb\\nc\\\\d\\re\nINPUT A1\nGET A1\n", "script"); Assert.Equal("A1\ta\\tb\\nc\\\\d\\re\nA1\tT:a\\tb\\nc\\\\d\\re\n", r.stdout); }
[Fact] public void Script_CrLfLines() => Assert.Equal("A1\tN:1\n", Run("SET A1 1\r\nGET A1\r\n", "script").stdout);
[Fact] public void Script_ErrContinues_Exit4() { var r = Run("GET A0\nFROB\nSET A1 1\nGET A1\n", "script"); Assert.Equal(4, r.code); var lines = r.stdout.Split('\n'); Assert.StartsWith("ERR\t", lines[0]); Assert.StartsWith("ERR\t", lines[1]); Assert.Equal("A1\tN:1", lines[2]); }
[Fact] public void Script_Phase2CommandsNotImplemented() => Assert.Equal("ERR\tnot implemented\n", Run("UNDO\n", "script").stdout);   // DELETE this test in Phase 2 Task 4
[Fact] public void Script_SaveLoad() { /* SAVE tmp; SET A1 9; LOAD tmp; GET A1 → original */ }
[Fact] public void Script_WithFile() { /* write file with A1=7; Run("GET A1\n","script","--file",path) → "A1\tN:7\n" */ }
[Fact] public void Eval_AddressesInOrder() { /* file A1=1,B1==A1+1 → eval f B1 a1 → "B1\tN:2\nA1\tN:1\n", exit 0 */ }
[Fact] public void Eval_All() { /* eval f --all → row-major lines */ }
[Fact] public void Eval_InvalidAddress_Exit5() { /* eval f A0 → code 5, stdout empty, stderr one line */ }
[Fact] public void Eval_MissingFile_Exit2() { }
[Fact] public void Eval_BadFormat_Exit3() { }
[Fact] public void Set_InPlaceAndOut() { /* set f A1 =1+1 --out g → g has A1, f unchanged; set f A1 3 → f updated */ }
[Fact] public void Usage_Exit1() { /* no args; unknown command "frob"; "eval" missing args; "set f A1" missing input */ }
```

`ProcessTests` builds the real `gridline.exe` from the test output by referencing the Cli project. It runs `gridline script` with a stdin of `SET A1 é\nGET A1\n` and asserts these things:
- The stdout bytes are UTF-8 without a BOM and contain no `\r`.
- The output is exactly `A1\tT:é\n`.
- The exit code is 0.

Write each commented test out fully using a temp directory fixture.

- [ ] **Step 2:** Run `dotnet test tests/Gridline.Cli.Tests -c Release`. Expected: FAIL.
- [ ] **Step 3:** Implement the CLI.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(cli): eval, set, script commands (§10)`.

---

### Task 13: Architecture tests (§3.2, §3.3)

**Files:**
- Test: `tests/Gridline.Architecture.Tests/ArchitectureTests.cs`. It references all `src` projects (App is added in Phase 2).

**Interfaces:**
- Consumes: the assemblies through `typeof(Workbook).Assembly`, `typeof(GridlineFile).Assembly`, and `typeof(CliApp).Assembly`.

One `[Fact]` per table row. Use `Types.InAssembly(asm).That().ResideInNamespace(ns)` combined with `.ShouldNot().HaveDependencyOnAny(...)` and assert `IsSuccessful`, printing the failing type names. These are the rules:
- Core must not depend on `Gridline.IO`, `Gridline.Cli`, `Gridline.App`, `System.Windows`, `System.IO.File`, `System.IO.Directory`, `System.IO.FileStream` or `System.Console`.
- IO must not depend on `Gridline.App`, `Gridline.Cli`, `System.Windows` or `System.Console`.
- Cli must not depend on `Gridline.App` or `System.Windows`.
- App must not depend on `Gridline.Cli`. Add this test in Phase 2.
- `Gridline.Core.Addressing` must not depend on `Gridline.Core.Parsing`, `Evaluation`, `Functions`, `Dependencies` or `Commands`.
- `Parsing` must not depend on `Evaluation`, `Functions`, `Dependencies` or `Commands`.
- `Evaluation` must not depend on `Commands` or `Dependencies`.
- `Functions` must not depend on `Parsing`, `Dependencies` or `Commands`.
- `Dependencies` must not depend on `Functions` or `Commands`.
- No public mutable static state: reflect over all `src` assemblies and fail on any `public static` non-readonly, non-const field, and on any public static property with a public setter.

NetArchTest's `HaveDependencyOn("System.IO.File")` is a prefix match, so it would also flag `System.IO.FileInfo`/`FileSystemInfo`/`FileAccess`. Core uses none of those, so the strict prefix is acceptable. Record that in DECISIONS.md.

- [ ] **Step 1:** Write all the tests.
- [ ] **Step 2:** Run `dotnet test tests/Gridline.Architecture.Tests -c Release`. Expected: PASS. If a rule fails, fix the production code, not the rule.
- [ ] **Step 3:** Sanity-check that the rules can fail. Temporarily add `System.Console.WriteLine` in a Core file, confirm the Core test fails, then revert.
- [ ] **Step 4:** Commit: `test(arch): NetArchTest rules (§3.2, §3.3)`.

---

### Task 14: Performance budgets (§12 P-1–P-5, P-7)

**Files:**
- Test: `tests/Gridline.Core.Tests/Performance/PerfTests.cs`, tagged `[Trait("Category","Perf")]`. P-1 and P-5 need IO, so put those in `tests/Gridline.IO.Tests/PerfTests.cs`.
- Modify: whatever production code profiling shows is hot.

Harness: `static TimeSpan Median(Action setup, Action measured, int runs = 5)`. It does one warm-up run, then 5 measured runs with `Stopwatch`. Each run calls `setup` outside the timing.

| Test | Setup | Assert |
|---|---|---|
| `P1_Load100k` | Build JSON bytes: A1:A50000 numbers; B1:B50000 `=A{i}+A{i+1}*2`, wrapping at the end | median `GridlineFile.Load` ≤ 1.5 s |
| `P2_Chain` | `FromSnapshot` with A1=1, A{i}=`=A{i-1}+1` up to 100000 | `SetCell("A1","2")` ≤ 250 ms; `EvaluatedFormulaCount == 99_999`; `GetValue("A100000")` is `N:100001` |
| `P3_NoDependents` | The P-1 workbook | median of 1,000 `SetCell("C{i}", i)` each ≤ 1 ms (measure each call, take the median) |
| `P4_SumEdit` | A1:A100000 numbers, B1=`=SUM(A1:A100000)` | `SetCell("A500","7")` ≤ 50 ms |
| `P5_Save` | 100k-cell workbook | `GridlineFile.SaveToPath` ≤ 1 s |
| `P7_ManySums` | 100k numbers + 1,000 × `=SUM($A$1:$A$100000)` | load ≤ 5 s; `Process.GetCurrentProcess().PrivateMemorySize64` ≤ 600 MB after `GC.Collect()` |

- [ ] **Step 1:** Write the perf tests.
- [ ] **Step 2:** Run `dotnet test Gridline.sln -c Release --filter Category=Perf`. Record the timings.
- [ ] **Step 3:** For each budget missed, profile, fix, and re-run. These are the likely fixes:
  - Avoid LINQ in hot paths.
  - Make range enumeration iterate only occupied cells.
  - On load, use a single `PlanAll` and skip per-cell event collection.
  - Pre-size dictionaries.
- [ ] **Step 4:** Commit: `perf: meet §12 budgets P-1..P-5, P-7`. Include the measured numbers in the message and in DECISIONS.md.

---

### Task 15: Phase 1 exit (§13)

- [ ] **Step 1:** Run `dotnet build Gridline.sln -c Release`. Expected: `0 Warning(s) 0 Error(s)`.
- [ ] **Step 2:** Run `dotnet test Gridline.sln -c Release --filter "Category!=UI"`. Expected: all tests pass.
- [ ] **Step 3:** Review `DECISIONS.md`. It must have one entry per decision flagged in this plan, each citing its `§`.
- [ ] **Step 4:** Commit: `chore: phase 1 complete (§13)`, then tag `phase-1`.
