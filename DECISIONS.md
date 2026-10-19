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

## Task 3: Tokenizer

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
- **FromSnapshot validation (§9.1).** All addresses, input lengths and duplicates (by cell key, so case-insensitive and `$`-insensitive) are checked before anything is built; an `""` input still counts for duplicates. Names are validated too (see the fix-wave entry on names; the original Phase 1 build rejected any name).
- **CoreTestHost (tests)** now uses the real `Workbook`: inputs are set in order, then the formula is set in Z100.

## Task 11: .gridline format and atomic save

- **Writer is hand-built (§8.1).** `GridlineFile.Serialize` appends to a `StringBuilder` using `JsonEscaper`, never `JsonSerializer`, so the bytes are exactly the spec layout (UTF-8 no BOM, LF, trailing `\n`).
- **Lone surrogates (§8.1).** `SetCell` accepts them and they cannot be UTF-8 encoded, so `JsonEscaper` writes them as `\udxxx` (lowercase hex); valid pairs are written literally. `System.Text.Json` refuses to read such a string (`GetString` throws `InvalidOperationException`), so the loader falls back to manually unescaping the raw token. The FsCheck round-trip property includes lone surrogates.
- **Missing keys (§8.2).** A missing `cells` or `names` key is treated as empty. If present, `names` must be an object with string values and `cells` an array, else `GridlineFormatException`.
- **Version (§8.2).** Accepted only if a JSON number that `TryGetInt64` accepts (so `1.5`, `1.0`, `1e0` and `"1"` are all rejected). `version > 1` gives a message containing `unsupported version`; `version < 1` gives `GridlineFormatException` ("invalid version").
- **Names (§8.1, §8.2; revised in the Phase 1 fix wave).** Entries of `names` are passed to `Workbook.FromSnapshot`, which validates them (§9.6); its `ArgumentException` for an invalid or duplicate name or `refersTo` is wrapped as `GridlineFormatException`. The writer emits `GetNames()` in the §8.1 layout, so a canonical file with names round-trips byte for byte.
- **Save (§8.4).** Temp file `path.<guid>.tmp` in the target directory (`FileMode.CreateNew`), `Flush(flushToDisk: true)`, `File.Move(tmp, path, overwrite: true)`; the temp is deleted best-effort on failure.
- **Undecodable strings and BOM (§8.2).** Any InvalidOperationException from System.Text.Json (invalid UTF-8 inside a string, lone-surrogate escapes in a name or format) is wrapped as GridlineFormatException. Property names and format go through the same tolerant unescaping as values, so a lone-surrogate name or format is handled like any other string. A leading UTF-8 BOM is skipped on load (lenient reading); the writer never emits one.

## Task 12: CLI Phase 1

- **Unknown escapes in SETX (§10.3).** `Unescape` handles `\`, `\t`, `\n`, `\r`; any other backslash sequence (and a trailing lone backslash) is left verbatim.
- **Exit codes for set (§10.1; revised in the Phase 1 fix wave).** `set` validates its arguments before touching the file, like `eval`: an invalid address gives 5 and input over 32,767 UTF-16 units gives 1 (usage), even for a missing or malformed file; then the file is loaded (missing 2, format 3). `eval` validates all addresses before loading the file.
- **Script input (§10.3).** stdin is read whole and split on `\n` only (a trailing `\r` is stripped per line), so a lone CR inside an input is preserved. Errors in SAVE/LOAD print an ERR line and the script continues; a failing LOAD keeps the current workbook.
- **ERR text (§10.3).** Messages are free-form, with tab/CR/LF replaced by spaces so each ERR is one line with exactly one tab.
- **Script command word and address** are case-insensitive; addresses are echoed in canonical form.

## Task 13: Architecture tests

- **Prefix matching (§3.2).** NetArchTest `HaveDependencyOn("System.IO.File")` is a prefix match, so it would also flag `System.IO.FileInfo`/`FileSystemInfo`/`FileAccess`. Core uses none of those, so the strict prefix is accepted.
- **Namespace filters (§3.3).** Per-namespace rules use the exact sub-namespace (`Gridline.Core.Parsing`, ...); `Gridline.Core.Addressing` forbids each sibling sub-namespace explicitly. Dependencies on root `Gridline.Core` types are not forbidden by §3.3 and are not checked. The Commands rules pass vacuously until Phase 2; the App rule is added in Phase 2.
- **Mutable static check (§3.2).** Reflects over Core, IO and Cli publicly visible types; flags public static fields that are neither const nor readonly and public static properties with a public setter, ignoring compiler-generated names containing `<`.

## Task 14: Performance budgets (§12 P-1–P-5, P-7)

- **Harness (§12).** `tests/Shared/PerfHarness.cs` is linked (`<Compile Include … Link>`) into Gridline.Core.Tests and Gridline.IO.Tests rather than duplicated. `Median(setup, measured, runs = 5)` does one warm-up, then 5 timed runs; `setup` and a full GC run before each run, outside the timing. P-3 times each of its 1,000 `SetCell` calls separately and takes the median. P-7 memory is `PrivateMemorySize64` after `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect()`, with the last loaded workbook still alive; the test host is included.
- **Serial perf runs.** Perf tests are in the `Perf` collection (`DisableParallelization = true`, so it runs alone after the parallel collections). Its collection fixture `PerfLock` holds an exclusive lock file in the temp directory, because `dotnet test Gridline.sln` runs the two test assemblies in parallel processes; the lock keeps Core and IO perf tests from overlapping. The two timed dependency-graph tests live in `DependencyGraphPerfTests`, also in the `Perf` collection (moved there in the Phase 1 fix wave).
- **Workloads.** P-1/P-3/P-5 use the brief's workbook: A1:A50000 = 1..50000, B{i} = `=A{i}+A{i+1}*2`, B50000 wraps to A1. P-1's JSON is built by hand in the test (not by the writer under test). P-2 and P-4 reuse one workbook: `setup` restores the original input (outside the timing), then the timed edit is applied. P-7 "load" is `GridlineFile.Load` of the hand-built file bytes (revised in the Phase 1 fix wave; it was `Workbook.FromSnapshot`), median of 5 loads; the test lives in Gridline.IO.Tests with P-1 and P-5. Measured ~1.5 s, 74 MB private memory.
- **Measured, then optimized only P-7 (Release, dev machine).** Before → after:
  P-1 load 162–245 ms → ~230 ms (budget 1.5 s; unchanged code path, run-to-run noise);
  P-2 chain edit 26 ms → 27–33 ms (250 ms);
  P-3 no-dependents edit median < 0.1 ms → 0.001 ms (1 ms);
  P-4 SUM edit 7.3 ms → 2.6 ms (50 ms);
  P-5 save 22.6 ms → 22.5 ms (1 s);
  P-7 load 2,423 ms → ~1,590 ms, private memory 46–60 MB (5 s, 600 MB).
  Only P-7 was within ~2x of its budget, so only it was optimized; every budget now has at least 3x headroom.
- **P-7 fixes.** (1) Aggregates (SUM, AVERAGE, MIN, MAX, PRODUCT, SUMSQ) fold numbers as they are enumerated through a struct `IAccumulator` instead of collecting them into a `List<double>` and calling LINQ. A 100k-cell SUM used to grow a list to 1 MB on the large object heap, and 1,000 of them allocated about 1 GB. The fold order (argument, then row-major) is the same as before; `Enumerable.Sum` over doubles is a sequential loop, so results are bit-identical. MIN/MAX keep the first extreme, like LINQ. (2) `CellStore` keeps each column's cells in a list parallel to its sorted row list, and `EvalContext.EnumerateNonBlank` reads a single-column range straight from those lists, with no dictionary lookup per cell. Multi-column ranges still use the key merge.

## Phase 1 final-review fix wave

- **BOM on script stdin (§10.3).** `gridline script` strips one leading U+FEFF from the stdin text before splitting lines, so a client writing with .NET's `Encoding.UTF8` (which emits a preamble) does not get `unknown command` on line 1. Only one BOM, only at the very start; a BOM anywhere else is ordinary input.
- **Whitespace-only script lines are blank (§10.3).** A line for which `string.IsNullOrWhiteSpace` holds (after the CR strip) is ignored like an empty line.
- **Unusable paths are I/O errors (§10.1).** An `ArgumentException`/`NotSupportedException` from path handling (empty path, embedded NUL, unsupported form) in `eval`, `set` (source or `--out`) or `script --file` maps to exit 2 with a single-line `error: invalid path: …` message (the path itself is not echoed, since it may contain NUL). Only the file read and `Path.GetFullPath` calls are wrapped, so an `ArgumentException` from anywhere else is still a bug, not exit 2. `GridlineFile.SaveToPath` resolves the target with `Path.GetFullPath` before creating the temp file, so an invalid target throws before anything is written and the temp file is always beside the target (never in the working directory).
- **Huge version numbers (§8.2).** `version` must be spelled as an integer (digits, optional leading `-`; no fraction or exponent), checked on the raw token before `TryGetInt64`. An integer too large for Int64 is reported as `unsupported version` (positive) or `invalid version` (negative), so `99999999999999999999` is a future version, not "not an integer". `1.0`, `1e0` and `1E20` are still "not an integer".
- **Names: Phase 1 storage and evaluation subset (§8.1, §8.2, §9.6; controller ruling).** Rejecting every file with a non-empty `names` object broke the §8.1 round trip, so Phase 1 now loads, stores, saves and evaluates names; there is still no public `DefineName`/`DeleteName` (Phase 3). `Gridline.Core.Addressing.NameRules` holds the rules Phase 3 will reuse: `IsValidName` (`^[A-Za-z_][A-Za-z0-9_.]{0,254}$`, not `TRUE`/`FALSE` case-insensitively, not `^[A-Za-z]{1,3}[0-9]+$`), `TryParseRefersTo` (fully absolute `$COL$ROW` or `$COL$ROW:$COL$ROW`, in bounds, letters any case, corners any order) and `FormatRefersTo` (canonical uppercase, normalized corners). `FromSnapshot` validates every name before building anything; names are unique under `OrdinalIgnoreCase`.
- **Canonical `refersTo` (§9.6).** Stored and saved canonical: `$a$1` → `$A$1`, `$C$10:$A$1` → `$A$1:$C$10`, `$A$01` → `$A$1`. A range whose corners coincide (`$A$1:$A$1`) is stored as the single cell `$A$1` and behaves like a cell name. A non-canonical file therefore loads fine but does not save byte-identically (only canonical files must).
- **`#REF!` names (§9.6).** `refersTo` exactly `#REF!` (case-sensitive, like error literals) is accepted so Phase 3 files round-trip. Such a name has no target (`SetNameTarget(name, null)`), is not a reference argument, and evaluates to `#REF!` (`SUM(Gone)` propagates it). The evaluator asks `IEvalContext.UnresolvedNameValue` for the value of any name `TryResolveName` does not resolve: `#NAME?` when undefined, `#REF!` for a `#REF!` name (default interface method returns `#NAME?`, so test contexts need no change).
- **Name evaluation and dependencies (§6.1, §6.7, §9.6).** `TryResolveName` looks names up case-insensitively; `isRange` is true when the target has more than one cell. Cell name ≈ cell reference, range name ≈ range (`=Total` alone → `#VALUE!`, `=SUM(Total)` aggregates), undefined → `#NAME?`. Each name's target is registered with `DependencyGraph.SetNameTarget` (upper-cased key) before the load recalc, so editing a cell inside a target recalculates the name's dependents, and a formula reaching its own cell through a name is a cycle (`#CIRC!`). Names do not count in `EvaluatedFormulaCount`.
- **Underscore names are lexically unreachable (§5.1 vs §9.6).** `_x` is a valid name, but §5.1 word tokens start with a letter, so a formula mentioning `_x` is `#PARSE!`. The name still loads, saves and appears in `GetNames()`. Not changed: the tokenizer follows §5.1 exactly.
- **Arity table is pinned by a test (§7.0–7.3, §5.4).** `ArityTests` writes the 50 Phase 1 arities out from the spec tables and checks them against the registry and end to end (min and max parse; min−1 and max+1 are `#PARSE!`; 256 args to a variadic is `#PARSE!`). Reading of the tables: a variadic `f(v1,…)` needs at least one argument (max 255); `TEXTJOIN(d,ignore_empty,…)` needs at least 3; `ROUND`/`ROUNDUP`/`ROUNDDOWN` require `d` (2–2); `IF` takes 2–3, `LEFT`/`RIGHT` 1–2, `FIND`/`SEARCH` 2–3, `SUBSTITUTE` 3–4.
