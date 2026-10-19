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

## Task 5: Coercion, comparison, evaluator

- **Blank flows internally; only the final result maps Blank→0 (§6.3, §6.6).** A reference to a blank cell (or a name bound to one, or a function returning Blank) yields `Blank`, so comparison's "blank adapts" rule sees it: `=A1=""` with A1 blank is TRUE. Arithmetic coerces Blank→0, `&` coerces Blank→"". `Evaluator.Evaluate` maps a top-level Blank to Number 0.
- **NumberGrammar uses ASCII digits only (§4.3 rule 5).** The regex `\d` is read as `[0-9]` (invariant culture); a hand scanner validates the shape, then `double.Parse` invariant parses the part before `%`. Non-finite results (`1e999`) are rejected; underflow (`1e-999`) is accepted as 0.
- **Left operand's coercion error beats the right operand's error (§6.4).** `"x"+#N/A` → `#VALUE!`: the left is evaluated and coerced before the right is evaluated. If the left is an error, the right is not evaluated (no observable difference; functions are deterministic).
- **`&` checks the 32,767 limit on every concatenation (§4.4)**, not only at the end, so a chain cannot build huge intermediates. The final result is also checked, covering text returned by functions.
- **Stack safety (§6.7 "recursion within a formula is acceptable").** Binary operators walk the left spine iteratively, so a left-deep chain such as 16,000 `+1` terms evaluates normally. Other recursion calls `RuntimeHelpers.EnsureSufficientExecutionStack()`; `InsufficientExecutionStackException` becomes `#VALUE!` for that formula.
- **Parenthesized references stay references (§7.0).** `Arg` unwraps `ParenNode`s before classifying, so `SUM((A1:B2))` gets a range argument (as Excel does). `(A1:B2)` in scalar position is still `#VALUE!`.
- **Arg shape (§7.0).** `IsReference` is true for a cell ref, range ref or defined name. An undefined name is classified as a direct argument (`IsReference` false, `Range` null), so `IsReference` ⇔ `Range != null` always holds and aggregation code can rely on it; `SUM(Nope)` propagates `#NAME?`. `Range` is a 1×1 `RangeRef` for a single-cell reference or a name bound to a cell, the normalized range for a range ref or range name, and null otherwise. A name's `Evaluate()` reuses the constructor's resolution, so shape and value cannot disagree. `Evaluate()` of an undefined name is `#NAME?`; of an omitted arg is Blank. Functions decide omitted-arg defaults themselves via `IsOmitted`.
- **Unknown function is decided by the Evaluator's dictionary (§5.4).** The name is upper-cased (invariant) and looked up; if absent the result is `#NAME?` and no argument is evaluated.
- **`Comparison.Compare` throws `ArgumentException` on an Error operand**; callers propagate errors first. `RangeRef.Get` throws `ArgumentOutOfRangeException` outside the range.

## Task 6: Function registry and Math functions

- **COUNTBLANK needs a reference argument (§7.1).** A direct argument (`COUNTBLANK(1)`) gives `#VALUE!`.
- **ROUNDUP when every digit lies below the rounding position (§7.1).** The brief's rule returns 0 when the cut is below 0; that is right for ROUND and ROUNDDOWN but wrong for ROUNDUP ("away from zero"). ROUNDUP of a non-zero `n` with such a cut returns `±10^(-digits)`, e.g. `ROUNDUP(0.001,-2)` = `100`, matching Excel.
- **Integer parameters saturate (§6.9).** `Coercion.TryToInt` truncates toward zero; values beyond the `int` range clamp to `int.MinValue`/`int.MaxValue`, so `ROUND(x,1E300)` returns `x` and `ROUND(x,-1E300)` returns 0.
- **Omitted arguments (§5.3, revised in fix round 1).** In variadic positions an omitted argument is absent and skipped (`SUM(,1)` = 1, `AVERAGE(1,)` = 1, `SUMPRODUCT(,)` = 0); COUNT/COUNTA do not count it. In fixed positions with no default (ABS, SIGN, INT, SQRT, MOD, POWER, ROUND*) an omitted argument is `#VALUE!` via `ArgGuard.Required`.
- **COUNT on a direct text argument** counts it only if it parses as a number under `NumberGrammar` (after trimming spaces), per the §7.1 "numeric text" rule.
- **SUMPRODUCT is sparse.** Each argument is mapped by position over its non-blank numeric cells; only positions present in every argument contribute, so cost is O(non-blank cells), not O(range area). Dimension mismatch beats an error value in a cell; otherwise the first error (argument order, then row-major) propagates. A scalar argument is a 1×1 array; booleans and text count as 0.
- **POWER reuses the `^` operator's logic** via `Evaluator.Power` (made internal), so `POWER(0,0)` is `#NUM!` and `POWER(0,-1)` is `#DIV/0!` as in §6.3.
- **Arity registry keys are upper-case**; `Register` upper-cases the name and throws on duplicates.
- **CoreTestHost (test side).** A mini interpreter of cell inputs stands in for the Workbook until Task 10 (blank, `'text`, TRUE/FALSE, number via NumberGrammar, `=formula` evaluated in listed order, else text).

## Task 7: Logic and information functions

- **IF omitted branches (§7.2, §5.3).** An omitted `t` (`IF(TRUE,,1)`) yields `0`; an omitted or absent `f` yields `FALSE`; an omitted condition is `#VALUE!`. Only the chosen branch is evaluated (`Arg` is lazy). The condition is coerced per §6.2, so `"x"` is `#VALUE!` and `"true"` is TRUE.
- **AND/OR/XOR scan (§7.2).** Reference args contribute booleans and numbers only (text and blanks are ignored; the first error propagates); direct args use §6.2 coercion; an omitted arg is absent and skipped (`AND(,TRUE)` = TRUE). NOT, IFERROR/IFNA (value, and alt when chosen) and the IS* tests give `#VALUE!` for an omitted argument (§5.3). No logical value found gives `#VALUE!`. XOR is TRUE for an odd count of TRUE values.
- **ISBLANK (§7.2)** is TRUE only for a single-cell reference (or a name bound to one) whose cell is blank; a range argument is FALSE.

## Task 8: Text functions and Wildcard

- **Wildcard (§7.3).** `IsMatch` matches the whole string; `Search` tries each start position >= start and matches a prefix of the rest (an empty match at `text.Length` counts, so `SEARCH("",t,LEN+1)` is `LEN+1`). Case-insensitive via `char.ToUpperInvariant`. A trailing lone `~` is a literal `~`. `IsMatch` uses iterative single-star backtracking. `Search` splits the pattern at unescaped stars: the first segment must match at the start position and later segments are found greedily leftmost; because tail success is monotone in the start, one tail failure returns -1, giving O(pattern*text) (the naive per-start retry was ~p*t^2 and hung on `SEARCH("a*a*a*a*b",REPT("a",32767))`). No regex, no recursion; `IsMatch` is the entry point Phase 3 reuses.
- **Omitted arguments (§5.3, revised in fix round 1).** Only documented defaults apply when a position is omitted (`LEFT`/`RIGHT` n=1, `FIND`/`SEARCH` s=1, `SUBSTITUTE` i = all). Every other omitted scalar position (text, number, boolean) is `#VALUE!` through `ArgGuard.Required`/`TryInt`, e.g. `MID("abc",2,)`, `REPT("a",)`, `FIND(,"abc")`. Variadic values (CONCAT, TEXTJOIN values) skip an omitted argument; TEXTJOIN's delimiter and `ignore_empty` are required.
- **32,767 limit (§4.4).** Every text-producing function checks the result; `REPT` and `SUBSTITUTE` check before/while building so huge intermediates are never allocated. A result of exactly 32,767 is allowed.
- **TEXTJOIN blanks.** With `ignore_empty` FALSE, blank cells in a range count as empty entries (delimiters are still emitted); they are accounted for from the sparse non-blank enumeration so a huge empty range is not scanned cell by cell. CONCAT skips blanks (they add nothing).
- **Range errors** inside CONCAT/TEXTJOIN ranges propagate (first in row-major order).
