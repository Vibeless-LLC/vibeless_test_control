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

## Task 9: Dependency graph and recalc planner

- **Range index: per-column row blocks plus a per-column tall list (§6.7, §12).** Ranges are normalized and deduplicated: one `RangeEntry` per distinct range, holding its dependent set (1,000 copies of `$A$1:$A$100000` are one entry with 1,000 dependents). Each column the range spans has a `ColumnIndex`. A short range (overlapping at most 8 blocks of 256 rows) is registered in each block it overlaps. A taller range goes into the column's `Tall` list. A changed cell scans only its own block plus its column's tall list, checking `Contains`. So sliding-window ranges (`SUM(A{i}:A{i+2})`) cost O(block occupancy) per lookup instead of O(all ranges), and a full-column SUM costs one entry. An entry leaves the index when its last dependent goes.
- **Names (§6.1).** A name is a virtual node with id `-(index+1)`. It is created the first time it is referenced or defined, and it is never removed, so dependents of an undefined name stay registered and `NameDependents` can re-plan them once the name is defined. A defined target is registered in the range index with the name node as a dependent. `DirectDependents` expands a name node into its formula dependents, so cycles through names are found like any other cycle. Name keys are case-insensitive (stored upper-cased).
- **`DirectDependents` may yield duplicates** (e.g. `=A1+SUM(A1:A2)`). The planner's visited state absorbs them, and skipping a per-call dedupe set keeps the no-dependents path (P-3) allocation-light.
- **One-pass planning (§6.7).** The brief describes a DFS that collects the affected set, then Tarjan over it. Tarjan's own explicit-stack DFS, started from the roots, visits exactly that reachable set, so the planner runs Tarjan directly from the roots. Roots are the changed formula cells, the dependents of changed non-formula cells, and the dependents of changed names. The result is the same and edges are enumerated once instead of twice. The range-chain graph has O(n²) edges, so this matters. Tarjan frames hold lazy dependent enumerators on a `List<Frame>`, so memory is O(depth + nodes), and there is no call-stack recursion. A self-edge marks a cycle, as does an SCC larger than 1. SCC emission order is reversed to give `Order`. `PlanAll` runs the same pass over all formula keys, ignoring edges to keys outside that set.
- **Bulk changes (§6.7, §12; Task 9 fix round 1).** `Plan` deduplicates roots, and it expands all changed non-formula cells through `DependencyGraph.DistinctDirectDependentsOfMany`. That method expands each distinct range entry and name node at most once and yields each dependent once. A paste or clear of all 100k cells under 1,000 × `=SUM($A$1:$A$100000)` therefore costs O(changed + entries hit + affected) and allocates about 1 MB, where the earlier version made about 10^8 roots and allocated about 1.1 GB.

## Task 10: Workbook Phase 1 API

- **One mutation path (§9.2, prepares §9.3).** `SetCell` validates, then calls a private `Commit(edits)` that applies every edit (store + graph), plans once with `RecalcPlanner.Plan(editedKeys, …)`, evaluates, sets `LastRecalc` and raises `CellsChanged`. Phase 2's `ApplyBatch`/undo/redo can reuse it. `FromSnapshot` shares the per-edit `Apply` but plans with `PlanAll`. The graph is fully updated before planning and never touched during planning or evaluation.
- **Setting the same input still recalculates (§9.2).** `LastRecalc` after `SetCell(X, …)` is the formula count of {X} ∪ dependents(X), so a repeated input is re-planned and re-evaluated; since nothing changes, no event is raised. A re-set formula starts from the cell's previous value, so an unchanged result is not reported as a value change.
- **EvaluatedFormulaCount counts every formula in the plan, cycle members included (§9.2).** Cycle members are assigned `#CIRC!` rather than evaluated, but they are formula cells in {X} ∪ dependents(X); after a load the count equals the total formula count as required. Changed constants are not counted.
- **Static dependencies (§6.1).** `DependencyExtractor` walks the AST with an explicit stack and collects every `RefNode`, normalized `RangeNode` and upper-cased `NameNode`, including untaken `IF` branches (so `=IF(TRUE,1,A1)` in A1 is a cycle). A parse error or a formula without references has null deps. A formula cell that becomes a constant or blank gets `SetDependencies(key, null)`.
- **CellStore range enumeration.** Ranges of at most 64 cells probe the dictionary directly; a single-column range binary-searches its column's sorted row list; wider ranges k-way merge the column lists with a `PriorityQueue` keyed by cell key (row-major). Cost is O(occupied in range × log cols + cols × log n). `KeysRowMajor` sorts the keys on demand (only `GetUsedAddresses` uses it).
- **CellsChanged** is built only when a handler is attached: changed keys (input changed, or value changed by `CellValue.Equals`) are sorted, deduplicated and converted to canonical addresses in a fresh array.
- **FromSnapshot validation (§9.1).** All addresses, input lengths and duplicates (by cell key, so case-insensitive and `$`-insensitive) are checked before anything is built; an `""` input still counts for duplicates. A non-empty `names` list throws `ArgumentException("names are not supported yet")` until Phase 3. `GetNames()` returns an empty list.
- **CoreTestHost (tests)** now uses the real `Workbook`: inputs are set in order, then the formula is set in Z100.

## Task 11: .gridline format and atomic save

- **Writer is hand-built (§8.1).** `GridlineFile.Serialize` appends to a `StringBuilder` using `JsonEscaper`, never `JsonSerializer`, so the bytes are exactly the spec layout (UTF-8 no BOM, LF, trailing `\n`).
- **Lone surrogates (§8.1).** `SetCell` accepts them and they cannot be UTF-8 encoded, so `JsonEscaper` writes them as `\udxxx` (lowercase hex); valid pairs are written literally. `System.Text.Json` refuses to read such a string (`GetString` throws `InvalidOperationException`), so the loader falls back to manually unescaping the raw token. The FsCheck round-trip property includes lone surrogates.
- **Missing keys (§8.2).** A missing `cells` or `names` key is treated as empty. If present, `names` must be an object with string values and `cells` an array, else `GridlineFormatException`.
- **Version (§8.2).** Accepted only if a JSON number that `TryGetInt64` accepts (so `1.5`, `1.0`, `1e0` and `"1"` are all rejected). `version > 1` gives a message containing `unsupported version`; `version < 1` gives `GridlineFormatException` ("invalid version").
- **Names in Phase 1 (§8.2).** Entries of `names` are passed to `Workbook.FromSnapshot`, which rejects any in Phase 1; the `ArgumentException` is wrapped as `GridlineFormatException`.
- **Save (§8.4).** Temp file `path.<guid>.tmp` in the target directory (`FileMode.CreateNew`), `Flush(flushToDisk: true)`, `File.Move(tmp, path, overwrite: true)`; the temp is deleted best-effort on failure.
- **Undecodable strings and BOM (§8.2).** Any InvalidOperationException from System.Text.Json (invalid UTF-8 inside a string, lone-surrogate escapes in a name or format) is wrapped as GridlineFormatException. Property names and format go through the same tolerant unescaping as values, so a lone-surrogate name or format is handled like any other string. A leading UTF-8 BOM is skipped on load (lenient reading); the writer never emits one.

## Task 12: CLI Phase 1

- **Unknown escapes in SETX (§10.3).** `Unescape` handles `\`, `\t`, `\n`, `\r`; any other backslash sequence (and a trailing lone backslash) is left verbatim.
- **Exit codes for set (§10.1).** `set` loads the file first (missing 2, format 3), then validates the address (invalid 5). Input over 32,767 UTF-16 units gives exit 1 (usage). `eval` validates all addresses before loading the file, so an invalid address gives 5 even for a missing file.
- **Script input (§10.3).** stdin is read whole and split on `\n` only (a trailing `\r` is stripped per line), so a lone CR inside an input is preserved. Errors in SAVE/LOAD print an ERR line and the script continues; a failing LOAD keeps the current workbook.
- **ERR text (§10.3).** Messages are free-form, with tab/CR/LF replaced by spaces so each ERR is one line with exactly one tab.
- **Script command word and address** are case-insensitive; addresses are echoed in canonical form.
