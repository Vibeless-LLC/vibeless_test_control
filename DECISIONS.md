# Decisions

Non-trivial implementation choices, with spec section references.

## Task 1: Addressing

- **Leading-zero rows accepted (§4.1).** `A01` parses as `A1` (Excel accepts it). Canonical `ToString()` yields `A1`.
- **Solution file format (§3.1).** `Gridline.sln` is the classic format (`dotnet new sln --format sln`), since the .NET 10 default is `.slnx` and the build contract names `Gridline.sln`.
- **Shared build settings (§3.1).** `Directory.Build.props` sets LangVersion latest, Nullable, ImplicitUsings, TreatWarningsAsErrors and InvariantGlobalization for all projects.
- **`CellAddress.TryParse(ReadOnlySpan<char>)` is `internal`** and used by `RangeAddress`; the public API is the `string` overload only (no allocation when parsing ranges).

## Task 2: Value types

- **`default(CellValue)` is Blank (§9.1).** String properties are backed by `_text ?? ""` / `_error ?? ""`, so `Text` and `Error` never return null.
- **`FromText(null)` throws `ArgumentNullException` (§9.1).** The spec is silent; a null text value has no canonical form.
- **`ErrorCodes.All` is an internal `FrozenSet<string>` (§4.5).** Immutable, so it is not mutable static state; `IsValid` is the public check and is ordinal and case-sensitive.
- **`CellValue` equality is ordinal and field-wise (§9.1).** `FromNumber` normalizes -0 and rejects non-finite values, so `Number.Equals` is safe (no NaN).
- **Tokenizer word scan and `$` placement (§5.1).** A word is `[$]?[A-Za-z][A-Za-z0-9_.]*`, followed by `$\d+` if a `$` comes next. A `$` anywhere else, a `$` with no digits after it, or a `$`-word that is not an in-bounds cell reference makes `TryTokenize` return false. A `$`-word followed directly by `(` is also a lexical error.
- **Tokenizer emits a trailing `End` token and tolerates odd numbers (§5.1).** An exponent is consumed only if digits follow it (`1e` lexes as `1`, then Name `e`), and the parser rejects the sequence. `Token.Pos` is the 0-based offset in the formula body.
- **TokenizerTests passes the expected kind as a string (§5.1).** `TokenKind` is internal and xUnit requires public test classes, so the theory parses the string with `Enum.Parse`.

## Task 4: Parser

- **Overflowing numeric literal is a parse error (§5.1, §5.4).** `1e999` parses to infinity under `double.Parse`; the parser rejects any non-finite `NumberNode` as `#PARSE!`.
- **Tokenizer failure is a parse error (§5.4).** `TryTokenize` returning false yields `ParsedFormula(null, true)`.
- **`:` is consumed only directly after a `CellRef` (§5.2).** Any other `:` (after a name, paren or number, or a third `:` in a chain) is left unconsumed and fails as a leftover or unexpected token. `A1:B2` keeps the corners as written.
- **Arity counts omitted args (§5.3, §5.4).** `PI()` has 0 args, `F(,)` has 2 omitted args. The arity lookup uses the upper-cased function name; unknown functions skip the check.
- **Deep nesting is a parse error, not a crash (§5.4).** The parser calls `EnsureSufficientExecutionStack` at expression and unary recursion and maps the resulting exception to `#PARSE!`.
- **Unary applies below `%` (§5.3).** `-2%` parses as `Percent(Unary(-,2))`, following the EBNF (`postfix = unary {"%"}`).
