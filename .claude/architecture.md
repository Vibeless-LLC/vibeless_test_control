# Gridline — Architecture & Specification

Spec version 1.0 · Covers Phases 1–3

---

## 0. Conventions

- **MUST / MUST NOT / SHOULD / MAY** are used as in RFC 2119.
- **Section numbers are requirement IDs.** Cite them in commits, tests, and decision records (e.g. `§6.3`).
- A **hidden acceptance suite** will test this implementation. It talks to the system only through:
  - the public API (§9);
  - the CLI (§10);
  - the file formats (§8);
  - the GUI automation surface (§11.7).

  Everything in those sections MUST be implemented exactly as written: names, signatures, output formats, error codes and exit codes. The suite is not available to you.
- Anything this document does not constrain is the implementer's choice. Record every non-trivial choice in `DECISIONS.md` at the repo root, with the section it relates to.
- Where this spec says "matches Excel", Excel for Microsoft 365 on Windows is the reference. Deliberate divergences from Excel are listed in §6.10.

---

## 1. Product summary

Gridline is a Windows desktop spreadsheet application with five parts:

- a single-sheet workbook with a formula engine of 70 built-in functions;
- incremental recalculation with cycle detection;
- undo/redo;
- a native JSON file format and CSV interchange;
- a WPF GUI and a headless CLI.

---

## 2. Technology stack

| Item | Requirement |
|---|---|
| SDK | .NET 10, C# (latest), `<Nullable>enable</Nullable>` in every project |
| Target frameworks | `Gridline.Core`, `Gridline.IO`, `Gridline.Cli`: `net10.0`. `Gridline.App`: `net10.0-windows` with `<UseWPF>true</UseWPF>` |
| Packages, `src/` | **None.** Only the .NET base class library is allowed, including `System.Text.Json` and `System.Text.Encoding.CodePages`. No third-party UI, parsing, or collection libraries. |
| Packages, `tests/` | `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FsCheck.Xunit`, `NetArchTest.Rules`, `FlaUI.UIA3`, `coverlet.collector` |
| Build | `dotnet build Gridline.sln -c Release` from the repo root MUST succeed with zero errors |
| Test | `dotnet test Gridline.sln -c Release` MUST pass. UI tests MAY be tagged `[Trait("Category","UI")]` and excluded from default runs. |
| CLI assembly name | `gridline`, so the build produces `gridline.exe` |

---

## 3. Solution layout and architecture rules

### 3.1 Layout

```
Gridline.sln
DECISIONS.md
src/
  Gridline.Core/        formula engine, model, recalculation, undo
  Gridline.IO/          .gridline file format, CSV
  Gridline.Cli/         console app (gridline.exe)
  Gridline.App/         WPF desktop app
tests/
  Gridline.Core.Tests/
  Gridline.IO.Tests/
  Gridline.Cli.Tests/
  Gridline.App.Tests/           (FlaUI, Category=UI)
  Gridline.Architecture.Tests/  (NetArchTest rules from §3.2–3.3)
```

### 3.2 Project dependency rules

| Project | May reference | MUST NOT depend on |
|---|---|---|
| Core | — | Any other Gridline project; `System.Windows*`; `System.IO.File`, `System.IO.Directory`, `System.IO.FileStream`, `System.Console` |
| IO | Core | App, Cli; `System.Windows*`; `System.Console` |
| Cli | Core, IO | App; `System.Windows*` |
| App | Core, IO | Cli |

### 3.3 Namespace rules inside Core

| Namespace | Responsibility | MUST NOT depend on |
|---|---|---|
| `Gridline.Core` | Public API types (§9.1) | — |
| `Gridline.Core.Addressing` | Address and range parsing and formatting | Every other `Gridline.Core.*` namespace |
| `Gridline.Core.Parsing` | Tokenizer, parser, AST, formula printer | `Evaluation`, `Functions`, `Dependencies`, `Commands` |
| `Gridline.Core.Evaluation` | Evaluator, coercion (§6) | `Commands`, `Dependencies` |
| `Gridline.Core.Functions` | Built-in functions (§7) | `Parsing`, `Dependencies`, `Commands` |
| `Gridline.Core.Dependencies` | Dependency graph, recalc scheduling, cycle detection | `Functions`, `Commands` |
| `Gridline.Core.Commands` | Undoable operations, undo stack | — |

Additional rules:

- No public mutable static state anywhere in `src/`.
- `Gridline.Architecture.Tests` MUST encode every rule in §3.2 and §3.3 as an automated NetArchTest test.

---

## 4. Data model

### 4.1 Grid

- Columns `A`–`ZZ` (1–702). Rows `1`–`100000`.
- One sheet per workbook.

### 4.2 Addresses and ranges

- A cell address is `[$]COLUMN[$]ROW`, case-insensitive. Examples: `a1`, `$B$7`, `zz100000`.
- The canonical form, used in all outputs, is uppercase with no `$`, e.g. `B7`.
- A range is `ADDR:ADDR`. The corners may be given in any order. A range is normalized to top-left:bottom-right, so `B3:A1` becomes `A1:B3`.
- An address outside §4.1 bounds is invalid.
- API methods given an invalid address or range throw `ArgumentException` and change nothing.

### 4.3 Cell input interpretation

Every cell stores its **input**: the exact string set by the user, at most 32,767 UTF-16 code units. The input is interpreted by applying these rules in order; the first match wins.

1. `""` → **blank**. The cell is removed.
2. Starts with `=` → **formula**. The remainder is parsed per §5. A lone `=` is a formula with a parse error.
3. Starts with `'` → **text**, equal to the input minus the leading apostrophe.
4. Equals `TRUE` or `FALSE`, case-insensitive → **boolean**.
5. Matches `^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?%?$` → **number**.
   - A trailing `%` divides the value by 100.
   - If the result is not finite, the input is text instead.
   - Matching uses the invariant culture: no thousands separators, no currency symbols, no surrounding whitespace.
6. Otherwise → **text**, verbatim. There is no trimming, so `" 5"` is text.

`GetInput` always returns exactly the string that was set. For formulas that string may later be rewritten under §5.5.

### 4.4 Values

A value is exactly one of the following:

| Kind | Rules |
|---|---|
| Blank | Only for cells with no input. A formula never yields Blank (§6.6). |
| Number | IEEE 754 double. `-0` is normalized to `0`. NaN and ±∞ are never stored; any computation producing them yields `#NUM!`. |
| Text | Maximum 32,767 code units. A longer result yields `#VALUE!`. |
| Boolean | `TRUE` or `FALSE`. |
| Error | One of the codes in §4.5. |

### 4.5 Error codes

| Code | Meaning |
|---|---|
| `#DIV/0!` | Division by zero, or AVERAGE over no numbers |
| `#VALUE!` | Wrong value type, or failed coercion |
| `#REF!` | Invalid reference, including one created by deleting rows or columns |
| `#NAME?` | Unknown function or undefined name |
| `#N/A` | Lookup value not found |
| `#NUM!` | Invalid numeric result or argument |
| `#CIRC!` | Cell is part of a circular reference (§6.7) |
| `#PARSE!` | Formula has a syntax error or a wrong argument count (§5.4) |

---

## 5. Formula language

### 5.1 Lexical rules

- Space and tab between tokens are ignored, with two exceptions: whitespace is not allowed inside string literals, and it is not allowed between a function name and its `(`.
- **Number literal**: `\d+(\.\d*)?([eE][+-]?\d+)?` or `\.\d+([eE][+-]?\d+)?`. Literals carry no sign; a leading sign is a unary operator.
- **String literal**: `"…"`. A doubled quote `""` inside the literal stands for one `"`.
- **Error literal**: any code from §4.5, written exactly. It evaluates to that error.
- **Word tokens**: `[$]?[A-Za-z][A-Za-z0-9_.]*` with an optional `$` before the row digits. A word token is classified as follows:
  - If it is immediately followed by `(` → **function name**, case-insensitive.
  - Else, if it matches a cell address within grid bounds (§4.2) → **cell reference**.
  - Else, if it equals `TRUE` or `FALSE`, case-insensitive → **boolean literal**.
  - Else → **name reference** (§9.6), case-insensitive. A name that is not defined evaluates to `#NAME?`.
  - A token containing `$` that is not a valid cell reference → `#PARSE!`.
- **Operators**: `:` `+` `-` `*` `/` `^` `&` `%` `=` `<>` `<` `>` `<=` `>=`.
- **Punctuation**: `(` `)` `,`.

### 5.2 Precedence and associativity

Listed from highest to lowest precedence:

| Level | Operators | Notes |
|---|---|---|
| 1 | `:` | Allowed only between two cell references. Anything else is `#PARSE!`. |
| 2 | unary `-`, unary `+` | |
| 3 | `%` (postfix) | |
| 4 | `^` | **Left-associative** |
| 5 | `*` `/` | Left-associative |
| 6 | `+` `-` | Left-associative |
| 7 | `&` | Left-associative |
| 8 | `=` `<>` `<` `>` `<=` `>=` | Left-associative |

Required consequences:

| Formula | Result | Why |
|---|---|---|
| `=-2^2` | `4` | Unary minus binds tighter than `^` |
| `=2^3^2` | `64` | `^` is left-associative |
| `=1+2&3` | text `"33"` | `+` binds tighter than `&` |
| `="a"&1=1` | `FALSE` | `&` binds tighter than `=` |
| `=50%%` | `0.005` | `%` applies twice |

### 5.3 Grammar (EBNF)

```
formula     = "=" expr ;
expr        = compare ;
compare     = concat { ("=" | "<>" | "<" | ">" | "<=" | ">=") concat } ;
concat      = additive { "&" additive } ;
additive    = multiplic { ("+" | "-") multiplic } ;
multiplic   = power { ("*" | "/") power } ;
power       = postfix { "^" postfix } ;
postfix     = unary { "%" } ;
unary       = ("-" | "+") unary | primary ;
primary     = number | string | boolean | error
            | cellref [ ":" cellref ]
            | name
            | funcname "(" [ arg { "," arg } ] ")"
            | "(" expr ")" ;
arg         = expr | (* empty → omitted argument *) ;
```

An omitted argument, as in `IF(A1,,3)`, is treated as if the argument were absent, so the function's default applies. If that position has no default, the result is `#VALUE!`.

### 5.4 Parse errors

These cases are not rejected at input time. The input is stored verbatim and the cell evaluates to `#PARSE!`:

- any syntax error;
- a known function called with an argument count outside its allowed range (§7);
- `:` used between anything other than two cell references.

An unknown function parses successfully and evaluates to `#NAME?`.

### 5.5 Canonical formula printing (Phase 2+)

When a formula must be rewritten (paste in §9.4, insert or delete in §9.5, name changes in §9.6), it is re-emitted in canonical form:

- `=` prefix. No whitespace anywhere except inside string literals.
- Function names, `TRUE`/`FALSE`, error literals and cell references are uppercase. Each reference keeps its own `$` markers.
- Name references use the casing of the name's definition, or the casing as written if the name is undefined.
- Number literals keep their exact original lexeme: `1.50` stays `1.50`.
- String literals are re-emitted with `""` escaping.
- Parentheses are preserved exactly as written. None are added or removed, so the AST MUST retain explicit parenthesized groups.
- A reference that became invalid is printed as `#REF!`. If either corner of a range became invalid, the whole range is printed as `#REF!`.

A formula that is not rewritten keeps its original text verbatim.

Example: `= sum( a1 , $b$2:C3 ) * (2.50+ x)` with `x` undefined, after a no-op rewrite, becomes `=SUM(A1,$B$2:C3)*(2.50+x)`.

---

## 6. Evaluation semantics

### 6.1 Static dependencies

A formula depends on every cell reference, range and name that appears anywhere in its text. This includes branches not taken by IF, CHOOSE or IFERROR. Dependencies do not depend on values.

### 6.2 Coercion

**To number**

| From | Result |
|---|---|
| Number | itself |
| Blank | `0` |
| Boolean | `1` or `0` |
| Text | Trim leading and trailing U+0020, then apply the §4.3 rule 5 number grammar (`%` included). Empty or non-matching text → `#VALUE!`. |
| Error | the error |

**To text**

| From | Result |
|---|---|
| Text | itself |
| Blank | `""` |
| Boolean | `"TRUE"` or `"FALSE"` |
| Number | `value.ToString("G15", CultureInfo.InvariantCulture)` |
| Error | the error |

**To boolean**

| From | Result |
|---|---|
| Boolean | itself |
| Blank | `FALSE` |
| Number | `TRUE` if non-zero |
| Text | `"TRUE"` or `"FALSE"`, case-insensitive. Other text → `#VALUE!` |
| Error | the error |

### 6.3 Operators

- **Arithmetic** (`+ - * /`): both operands are coerced to number.
  - Division by zero → `#DIV/0!`.
  - A non-finite result → `#NUM!`.
- **Unary operators and `%`**:
  - Unary `-` coerces to number.
  - Unary `+` returns its operand unchanged, with no coercion.
  - `%` coerces to number, then divides by 100.
- **Power** (`^`):
  - Both operands are coerced to number.
  - `0^0` → `#NUM!`.
  - `0^negative` → `#DIV/0!`.
  - A negative base with a non-integer exponent → `#NUM!`.
  - Overflow → `#NUM!`.
- **Concatenation** (`&`): both operands are coerced to text.
- **Comparison**:
  - If one operand is blank, it adapts to the other operand's kind: `0`, `""` or `FALSE`. Blank equals blank.
  - Different kinds order as **Number < Text < Boolean**.
  - Text compares case-insensitively: ordinal comparison of `ToUpperInvariant()`.
  - Numbers compare as exact IEEE values.
  - The result is a Boolean.

### 6.4 Error propagation

- Operators evaluate the left operand, then the right. The first error encountered is the result.
- Functions propagate the first error, in argument order. Within a range, cells are visited in row-major order. Error-handling functions are the exception (§7).

### 6.5 Ranges

- A range is valid only as a function argument where §7 accepts a range.
- A range used anywhere else (an operator operand, or the top-level result as in `=A1:B2`) → `#VALUE!`.

### 6.6 References to blank cells

- A reference to a blank cell used as a scalar yields number `0`. For example, `=A1` with A1 blank gives `0`.
- A formula never yields Blank.

### 6.7 Recalculation and cycles

- Recalculation is **incremental and synchronous**. When a cell changes, the cell itself and all of its transitive dependents are re-evaluated before the API call returns. No other cells are re-evaluated.
- Each formula cell is evaluated **at most once** per recalc, in dependency (topological) order.
- Editing any cell inside a referenced range, including a blank cell, triggers dependents of that range.
- **Cycles**:
  - A cell is in a cycle if it is reachable from itself through static dependencies (§6.1). This includes self-reference and paths through ranges and names.
  - Every cell in a cycle evaluates to `#CIRC!`.
  - Cells that depend on a cycle receive `#CIRC!` as an ordinary error value, so `=IFERROR(A1,0)` works.
  - Breaking a cycle restores normal values on the next recalc.
- **Depth**:
  - Recalculation, dependency traversal and cycle detection MUST NOT use call-stack recursion proportional to chain length.
  - A dependency chain of 100,000 cells MUST evaluate without stack overflow.
  - Recursion within a single formula's AST is acceptable.
- **Load**: loading a file performs a full recalc.

### 6.8 Determinism

All functions are deterministic. There are no volatile functions such as `TODAY`, `NOW` or `RAND`.

### 6.9 Integer parameters

Wherever a function expects an integer (counts, positions, indexes, digits), the argument is coerced to number and then **truncated toward zero**.

### 6.10 Deliberate divergences from Excel

The hidden suite's Excel-oracle tests exclude these behaviors and test them against this spec instead:

1. Circular references yield `#CIRC!`. Excel warns and shows `0`.
2. Ranges in scalar context yield `#VALUE!`. There is no implicit intersection and no dynamic-array spilling. This also covers INDEX with row 0 or column 0, INDEX on a 2-D range with the column omitted, and CHOOSE returning a range.
3. Invalid formulas are stored and yield `#PARSE!`. Excel rejects them at entry.
4. Equality is exact IEEE. Excel applies 15-digit tolerance in some comparisons.
5. Text ordering is ordinal on uppercase invariant. Excel uses locale collation, which differs for punctuation.
6. Number-to-text conversion uses `G15`. Exponent formatting may differ from Excel for |x| < 1e-4 or |x| ≥ 1e15.
7. Text is never parsed as a date. `"2024-01-01"+0` is `#VALUE!`.
8. In criteria (§7.5), a numeric criterion matches numeric cells only.

---

## 7. Function library (70 functions)

### 7.0 General rules

- Function names are case-insensitive.
- Arity limits are enforced at parse time (§5.4).
- Variadic functions accept at most 255 arguments.
- **Reference argument vs. direct argument.** An argument that is a cell reference, range reference or name is a **reference argument**. Any other expression is a **direct argument**.
- **Aggregation rules** apply where a function is marked "agg" below:

| Argument type | Number | Boolean | Text | Blank | Error |
|---|---|---|---|---|---|
| Reference | included | ignored | ignored | ignored | propagates |
| Direct | included | coerced to `1`/`0` | coerced to number or `#VALUE!` | `0` | propagates |

### 7.1 Math (Phase 1) — 20 functions

| Function | Semantics |
|---|---|
| `SUM(v1,…)` | agg. With no numbers → `0`. |
| `AVERAGE(v1,…)` | agg. With no numbers → `#DIV/0!`. |
| `MIN(v1,…)` / `MAX(v1,…)` | agg. With no numbers → `0`. |
| `PRODUCT(v1,…)` | agg. With no numbers → `0`. |
| `COUNT(v1,…)` | Counts numbers. Reference args: numbers only, errors **ignored**. Direct args: numbers, booleans and numeric text count; everything else, including errors, is ignored. Never errors. |
| `COUNTA(v1,…)` | Counts non-blank values, including errors and `""` text produced by formulas. |
| `COUNTBLANK(range)` | Counts blank cells plus cells whose value is text `""`. Takes exactly one range or cell reference. |
| `SUMPRODUCT(r1,…)` | All ranges must have identical dimensions, else `#VALUE!`. A scalar argument is treated as 1×1. Non-numbers count as `0`. Errors propagate. |
| `ABS(n)` | Absolute value. |
| `SIGN(n)` | `-1`, `0` or `1`. |
| `INT(n)` | Floor toward −∞: `INT(-2.5)` = `-3`. |
| `MOD(n,d)` | `n - d*INT(n/d)`, so the sign follows the divisor: `MOD(-3,2)` = `1`, `MOD(3,-2)` = `-1`. `d = 0` → `#DIV/0!`. |
| `POWER(b,e)` | Same as `^` (§6.3). |
| `SQRT(n)` | `n < 0` → `#NUM!`. |
| `PI()` | `Math.PI`. |
| `ROUND(n,d)` | Half **away from zero**, applied to the **shortest round-trip decimal representation** of `n`. `d` may be negative. Examples: `ROUND(2.675,2)` = `2.68`, `ROUND(0.285,2)` = `0.29`, `ROUND(-2.5,0)` = `-3`, `ROUND(1250,-2)` = `1300`. |
| `ROUNDUP(n,d)` | Away from zero, same decimal basis as ROUND. |
| `ROUNDDOWN(n,d)` | Toward zero, same decimal basis as ROUND. |
| `SUMSQ(v1,…)` | agg. Sum of squares. With no numbers → `0`. |

### 7.2 Logic and information (Phase 1) — 13 functions

| Function | Semantics |
|---|---|
| `IF(c,[t],[f])` | Takes 2–3 arguments. `c` is coerced to boolean. **Lazy**: only the chosen branch is evaluated. An omitted `f` → `FALSE`. An omitted `t` → `0`. |
| `AND(…)` / `OR(…)` / `XOR(…)` | Reference args: booleans count, numbers are coerced (non-zero is TRUE), text and blanks are ignored. Direct args are coerced per §6.2. If no logical values were found → `#VALUE!`. Errors propagate. |
| `NOT(v)` | Boolean negation after coercion. |
| `IFERROR(v,alt)` | If `v` is any error, returns `alt`. `alt` is evaluated lazily. |
| `IFNA(v,alt)` | Same as IFERROR, but only for `#N/A`. |
| `ISBLANK(v)` | `TRUE` only for a reference to a truly blank cell. A `""` formula result is not blank. |
| `ISNUMBER` / `ISTEXT` / `ISLOGICAL` / `ISERROR` / `ISNA` `(v)` | Type tests. Never return an error. |

### 7.3 Text (Phase 1) — 17 functions

Positions and lengths are 1-based, counted in UTF-16 code units. Arguments are coerced to text or number as their role requires.

| Function | Semantics |
|---|---|
| `LEN(t)` | Length. |
| `LEFT(t,[n=1])` / `RIGHT(t,[n=1])` | `n < 0` → `#VALUE!`. `n > LEN` → the whole string. |
| `MID(t,s,n)` | `s < 1` or `n < 0` → `#VALUE!`. `s > LEN` → `""`. |
| `UPPER(t)` / `LOWER(t)` | Invariant culture. |
| `PROPER(t)` | A letter (`char.IsLetter`) is uppercased if the preceding character is not a letter, or if it is first; otherwise it is lowercased. Non-letters are unchanged. `"o'neil 2nd"` → `"O'Neil 2Nd"`. |
| `TRIM(t)` | Removes leading and trailing U+0020 and collapses internal runs of U+0020 to one. Other whitespace is untouched. |
| `CONCAT(…)` | Text-coerces every value; ranges are read row-major; blanks become `""`. Result > 32,767 → `#VALUE!`. |
| `TEXTJOIN(d,ignore_empty,…)` | Like CONCAT with delimiter `d`. If `ignore_empty` (coerced to boolean) is TRUE, blanks and `""` are skipped. |
| `FIND(f,t,[s=1])` | Case-sensitive, no wildcards. `s < 1` or `s > LEN(t)+1` → `#VALUE!`. Not found → `#VALUE!`. `FIND("",t,s)` = `s`. |
| `SEARCH(f,t,[s=1])` | Like FIND, but case-insensitive with wildcards: `*` matches any sequence, `?` matches one character, `~` escapes the next character. |
| `SUBSTITUTE(t,old,new,[i])` | Case-sensitive. If `old` is `""`, returns `t`. If `i` is given, replaces only the i-th non-overlapping occurrence, counted left to right. `i < 1` → `#VALUE!`. |
| `REPLACE(t,s,n,new)` | `s < 1` or `n < 0` → `#VALUE!`. `s` beyond the end appends. |
| `REPT(t,n)` | `n < 0` → `#VALUE!`. Result > 32,767 → `#VALUE!`. |
| `EXACT(a,b)` | Case-sensitive ordinal equality. |
| `VALUE(t)` | Coerces to number per §6.2. Numbers pass through. |

### 7.4 Lookup and reference (Phase 3) — 9 functions

| Function | Semantics |
|---|---|
| `VLOOKUP(v,table,col,[approx=TRUE])` | **Approximate**: binary search over the first column, assumed ascending; returns the last row whose key ≤ `v` under §6.3 ordering. If `v` is less than the first key → `#N/A`. Results on unsorted or mixed-type columns are unspecified. **Exact**: first match; text matching is case-insensitive with wildcards `* ? ~`. Not found → `#N/A`. `col < 1` → `#VALUE!`. `col` greater than the table width → `#REF!`. |
| `HLOOKUP(v,table,row,[approx=TRUE])` | VLOOKUP transposed. |
| `INDEX(range,r,[c])` | Returns a value, never a reference. For a single-row or single-column range, one index selects the element. Out of bounds → `#REF!`. `r` or `c` equal to `0` → `#VALUE!`. A 2-D range with `c` omitted → `#VALUE!`. |
| `MATCH(v,range,[type=1])` | `range` must be a single row or column, else `#N/A`. `1`: largest value ≤ `v` (ascending). `0`: exact match, with wildcards. `-1`: smallest value ≥ `v` (descending). Returns a 1-based position. Not found → `#N/A`. |
| `CHOOSE(i,v1,…)` | Evaluates only the chosen value. `i < 1` or `i > count` → `#VALUE!`. |
| `ROW([ref])` / `COLUMN([ref])` | Row or column number of the reference's top-left cell. Omitted → the formula's own cell. |
| `ROWS(range)` / `COLUMNS(range)` | Dimensions. |

### 7.5 Conditional aggregation (Phase 3) — 3 functions

`SUMIF(range,criteria,[sum_range])`, `COUNTIF(range,criteria)`, `AVERAGEIF(range,criteria,[avg_range])`.

**Criteria as text.** A criteria string is an optional operator (`=`, `<>`, `<`, `>`, `<=`, `>=`) followed by an operand. The operand is:

- a number, if it matches the §4.3 rule 5 grammar;
- a boolean, if it is `TRUE` or `FALSE`;
- text otherwise.

**Matching:**

| Operand | Operator | Matches |
|---|---|---|
| Number | any | Numeric cells only, compared with that operator |
| Text | `=` or none | Text cells, case-insensitive, wildcards `* ? ~` |
| Text | `<>` | Every cell that does not match, including blanks |
| Text | `<` `>` `<=` `>=` | Text cells only, case-insensitive ordering |
| Empty (`""` or `"="`) | — | Blank cells and `""` text |
| `"<>"` alone | — | Non-blank cells |

**Criteria as a value.** A criteria that is a number or boolean value, not text, means equality.

**Other rules:**

- `sum_range` and `avg_range` are anchored at their top-left cell and resized to `range`'s dimensions. Only numbers are aggregated.
- Error cells in `range` never match. An error in `sum_range` at a matched position propagates.
- AVERAGEIF with no matches → `#DIV/0!`.

### 7.6 Date (Phase 3) — 8 functions

**Serial system.** The 1900 system matches Excel exactly, **including the fictitious 1900-02-29**:

| Serial | Date |
|---|---|
| 1 | 1900-01-01 |
| 59 | 1900-02-28 |
| 60 | 1900-02-29 (fictitious) |
| 61 | 1900-03-01 |
| 0 | "1900-01-00" |

Valid serials are `0`–`2958465`, i.e. up to 9999-12-31. Arguments are numbers truncated per §6.9. Text is never parsed as a date.

| Function | Semantics |
|---|---|
| `DATE(y,m,d)` | `0 ≤ y ≤ 1899` → `y+1900`. `y < 0` or `y ≥ 10000` → `#NUM!`. Month and day overflow roll over: `DATE(2024,14,1)` = 2025-02-01, `DATE(2024,1,0)` = 2023-12-31. A result outside the valid serial range → `#NUM!`. |
| `YEAR(s)` / `MONTH(s)` / `DAY(s)` | Serial outside the valid range → `#NUM!`. Serial `60` → 1900, 2, 29. Serial `0` → 1900, 1, 0. |
| `WEEKDAY(s,[type=1])` | Type 1: `((s-1) floormod 7)+1`, with Sunday = 1 and serial 1 a Sunday. Type 2: Monday = 1 … Sunday = 7. Type 3: Monday = 0 … Sunday = 6. Any other type → `#NUM!`. |
| `EDATE(s,months)` | Same day-of-month, `months` later, clamped to the end of the target month. Result out of range → `#NUM!`. Behavior for `s < 61` is unspecified. |
| `EOMONTH(s,months)` | Last day of the month `months` away. Same range rules as EDATE. |
| `DAYS(end,start)` | `INT(end) - INT(start)`. |

---

## 8. Persistence and formats

### 8.1 `.gridline` file format, version 1 (Phase 1)

- UTF-8 JSON, no BOM, LF line endings, with a trailing newline.
- **Canonical serialization is byte-exact.** Saving the same workbook MUST always produce identical bytes, and saving an unmodified loaded canonical file reproduces it byte-for-byte.

Layout:

```
{
  "format": "gridline",
  "version": 1,
  "names": {
    "Rate": "$B$1",
    "Total": "$A$1:$A$10"
  },
  "cells": [
    ["A1", "=SUM(B1:B3)*Rate"],
    ["B1", "5"],
    ["A2", "hello \"world\""]
  ]
}
```

Rules:

- Two-space indent. Key order is `format`, `version`, `names`, `cells`.
- Empty collections are written as `"names": {},` and `"cells": []`.
- `names` are sorted by `StringComparer.OrdinalIgnoreCase`, with each name in its defined casing. Phase 1 writes `{}`.
- `cells` are in row-major order. Each entry is `[address, input]` on its own line, with canonical address and exact input.
- **String escaping**:
  - `"` is written as `\"` and `\` as `\\`.
  - U+0008, U+000C, U+000A, U+000D and U+0009 are written as `\b`, `\f`, `\n`, `\r` and `\t`.
  - Other U+0000–U+001F characters are written as `\u00xx` with lowercase hex.
  - **Every other character is written literally**, including non-ASCII, `<`, `>`, `&`, `'` and `+`.

### 8.2 Loading (Phase 1)

- Any valid JSON is accepted, with any whitespace, key order or escaping, as long as it satisfies the schema. Unknown top-level keys are ignored.
- Each of the following throws `GridlineFormatException` and loads nothing (no partial state):
  - malformed JSON;
  - `format` is not `"gridline"`;
  - `version` is missing or not an integer;
  - `version > 1` — the message MUST contain `unsupported version`;
  - a cell entry that is not a two-string array;
  - an invalid address;
  - a duplicate address (case-insensitive);
  - an input longer than 32,767;
  - an invalid name or `refersTo` (§9.6).
- After a load: a full recalc has run, the workbook is not dirty, and undo history is empty.

### 8.3 Canonical value strings

These are used by `CellValue.ToCanonicalString()` and by the CLI.

| Kind | Format |
|---|---|
| Blank | `BLANK` |
| Number | `N:` + `value.ToString("R", CultureInfo.InvariantCulture)`, with `-0` printed as `0` |
| Text | `T:` + escaped text, with `\` → `\\`, tab → `\t`, LF → `\n`, CR → `\r` |
| Boolean | `B:TRUE` or `B:FALSE` |
| Error | `E:` + code, e.g. `E:#DIV/0!` |

### 8.4 Atomic save (Phase 1)

`SaveToPath` MUST:

1. write to a temporary file in the same directory;
2. flush it to disk with `Flush(flushToDisk: true)`;
3. replace the target in one step (`File.Move(tmp, path, overwrite: true)` or `File.Replace`).

If the process is killed at any instant, the target file is either the complete old content or the complete new content. Leftover temp files are acceptable.

### 8.5 CSV (Phase 3)

**Import**

- **Encoding**, detected in this order:
  1. UTF-8 BOM → UTF-8;
  2. UTF-16 LE BOM;
  3. UTF-16 BE BOM;
  4. no BOM and the bytes are valid UTF-8 → UTF-8;
  5. otherwise Windows-1252.
- **Records** are separated by CRLF, LF or CR, in any mix. A final terminator does not create an empty row.
- **Fields** follow RFC 4180 using the configured delimiter:
  - Quoted fields may contain the delimiter, `""` and line breaks.
  - A `"` inside an unquoted field is a literal character.
  - An unterminated quoted field at EOF → `GridlineFormatException`.
- **Mapping**: field *j* of record *i* goes to the cell at row *i*, column *j*.
  - An empty field → blank.
  - A field starting with `=` or `'` is stored as input `"'" + field`. This blocks formula injection and round-trips text.
  - Every other field is stored verbatim, so §4.3 interprets it: `-5` becomes a number, `5%` becomes 0.05, `true` becomes a boolean.
- More than 100,000 records or 702 fields in a record → `GridlineFormatException`.

**Export**

- **Extent**: rectangle from A1 to the maximum used row and column.
- **Cell values**:
  - Exported as computed values, not inputs.
  - Blank → empty field.
  - Number → `ToString("R", InvariantCulture)`.
  - Boolean → `TRUE` or `FALSE`.
  - Text → as is.
  - Error → its code.
- **Quoting**: a field is quoted if and only if it contains the delimiter, `"`, CR or LF, or begins or ends with U+0020. Quotes inside are doubled.
- **Output**: CRLF after every record, including the last. UTF-8 **with** BOM. An empty workbook produces only the BOM.
- **Delimiter**: any character except `"`, CR and LF; otherwise `ArgumentException`.

---

## 9. Public API (normative)

### 9.1 Gridline.Core

```csharp
namespace Gridline.Core;

public enum ValueKind { Blank, Number, Text, Boolean, Error }

public static class ErrorCodes
{
    public const string Div0 = "#DIV/0!", Value = "#VALUE!", Ref = "#REF!", Name = "#NAME?",
                        NA = "#N/A", Num = "#NUM!", Circ = "#CIRC!", Parse = "#PARSE!";
}

public readonly struct CellValue : IEquatable<CellValue>
{
    public ValueKind Kind { get; }
    public double Number { get; }     // 0 unless Kind == Number
    public string Text { get; }       // "" unless Kind == Text
    public bool Boolean { get; }      // false unless Kind == Boolean
    public string Error { get; }      // "" unless Kind == Error
    public static CellValue Blank { get; }
    public static CellValue FromNumber(double value);  // -0 → 0; NaN/±∞ → Error #NUM!
    public static CellValue FromText(string value);
    public static CellValue FromBoolean(bool value);
    public static CellValue FromError(string code);    // ArgumentException if not in ErrorCodes
    public string ToCanonicalString();                 // §8.3
}

public readonly record struct RecalcStats(int EvaluatedFormulaCount);

public sealed class CellsChangedEventArgs : EventArgs
{
    public IReadOnlyCollection<string> Addresses { get; }  // canonical addresses
}

public sealed class Workbook
{
    public const int MaxRows = 100_000;
    public const int MaxColumns = 702;
    public const int MaxInputLength = 32_767;
    public const int UndoLimit = 100;

    public Workbook();

    // ---- Phase 1 ----
    public void SetCell(string address, string input);       // "" clears
    public string GetInput(string address);                   // "" if blank
    public CellValue GetValue(string address);
    public IReadOnlyList<string> GetUsedAddresses();          // cells with input, row-major
    public RecalcStats LastRecalc { get; }
    public event EventHandler<CellsChangedEventArgs>? CellsChanged;

    // ---- Phase 2 ----
    public void ApplyBatch(IReadOnlyList<KeyValuePair<string, string>> edits);
    public void CopyPaste(string sourceRange, string destinationTopLeft);
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    public bool Undo();                                       // false if nothing to undo
    public bool Redo();
    public bool IsDirty { get; }
    public void MarkSaved();

    // ---- Phase 3 ----
    public void InsertRows(int row, int count);
    public void DeleteRows(int row, int count);
    public void InsertColumns(int column, int count);         // 1-based column number
    public void DeleteColumns(int column, int count);
    public void DefineName(string name, string refersTo);
    public void DeleteName(string name);
    public IReadOnlyList<KeyValuePair<string, string>> GetNames(); // sorted OrdinalIgnoreCase
}
```

### 9.2 General API semantics (Phase 1)

- **Atomicity.** Any method that throws leaves the workbook completely unchanged.
- **SetCell input.** Input longer than `MaxInputLength` → `ArgumentException`.
- **Mutation.** Every mutating call recalculates synchronously (§6.7) before returning.
- **CellsChanged** is raised once per mutating call, after recalc. It carries every address whose input or value changed. It is not raised if nothing changed.
- **`LastRecalc.EvaluatedFormulaCount`** is the number of formula evaluations performed by the most recent mutating call or load.
  - After `SetCell(X, …)` it equals the number of formula cells in {X} ∪ transitive-dependents(X). Setting a constant with no dependents gives `0`.
  - After a load it equals the total number of formula cells.
- **Concurrency.** Single-threaded use only. No thread safety is required.

### 9.3 Undo and dirty tracking (Phase 2)

- Each mutating API call is **one undo step**: SetCell, ApplyBatch, CopyPaste, insert/delete, DefineName and DeleteName.
- Calls that change nothing (for example, setting the same input) create no step.
- **Undo** restores the exact prior inputs, including formula text verbatim, and the exact prior names. **Redo** reapplies the step.
- A new mutation after an undo clears the redo stack.
- At most `UndoLimit` steps are kept. When a 101st is added, the oldest is dropped.
- **Dirty tracking**:
  - A new or freshly loaded workbook is not dirty.
  - `MarkSaved()` records the current history position as "saved".
  - `IsDirty` is true exactly when the current position differs from the saved position. Undoing back to the saved position therefore makes the workbook clean again.
  - If the saved position has been dropped from history, the workbook is dirty until the next `MarkSaved()`.
- **ApplyBatch**:
  - If an address appears more than once, the last edit wins.
  - An empty batch is a no-op and creates no undo step.
  - Any invalid address → `ArgumentException`, and nothing is applied.

### 9.4 CopyPaste (Phase 2)

- The source range is copied to the same-sized rectangle whose top-left is the destination. The source is snapshotted first, so overlapping source and destination work correctly.
- If the destination rectangle exceeds the grid → `ArgumentException`.
- **Non-formula inputs** are copied verbatim. **Blank** source cells clear their destination cells.
- **Formula inputs**:
  - Each reference is shifted by the offset (Δrow, Δcol). Absolute components (`$`) do not shift; mixed references shift only their relative part.
  - A reference that shifts outside the grid becomes `#REF!` (§5.5).
  - Every pasted formula is re-emitted in canonical form (§5.5), even when the offset is zero.
- The paste is one undo step.

### 9.5 Insert and delete rows/columns (Phase 3)

Rows are described below. Columns are symmetric.

**`InsertRows(row, count)`**

- Requires `1 ≤ row ≤ MaxRows` and `count ≥ 1`, else `ArgumentOutOfRangeException`.
- If any cell with input would move past `MaxRows` → `InvalidOperationException`, and nothing changes.
- Cells at rows ≥ `row` move down by `count`.

**`DeleteRows(row, count)`**

- Requires `1 ≤ row` and `row + count - 1 ≤ MaxRows`.
- Rows `[row, row+count-1]` are removed. Cells below them move up.

**Reference adjustment** applies to every reference in every formula and in every name definition. Relative and absolute references are treated the same way. References point at positions, so moving a formula does not change this.

| Case | Insert at `r`, `n` rows | Delete `[r, r+n-1]` |
|---|---|---|
| Single cell at row `x` | `x ≥ r` → `x + n`. If the result exceeds `MaxRows` → `#REF!` | `x` inside the deleted span → `#REF!`. `x` below it → `x − n` |
| Range | Each endpoint independently: `≥ r` → `+n`. Example: insert at 3 makes `A1:A5` → `A1:A7`; insert at 1 makes it `A3:A7` | Keep the surviving rows. If none survive → `#REF!`. Otherwise the new endpoints are the first and last surviving rows after the shift. Examples: delete 3–4 makes `A1:A10` → `A1:A8`; delete 1–2 makes it `A1:A8` |

- Formulas with at least one changed reference are re-emitted per §5.5. Formulas with no changed references keep their text verbatim.
- The operation is one undo step.

### 9.6 Names (Phase 3)

**Name syntax**

- Matches `^[A-Za-z_][A-Za-z0-9_.]{0,254}$`.
- Must not be `TRUE` or `FALSE`.
- Must not match `^[A-Za-z]{1,3}[0-9]+$`, because that looks like a reference.
- Names are unique case-insensitively.
- An invalid name → `ArgumentException`.

**`refersTo`**

- Must be a fully absolute cell or range, such as `$B$1` or `$A$1:$C$10`.
- Stored in canonical uppercase. Anything else → `ArgumentException`.

**Defining and deleting**

- `DefineName` on an existing name replaces its definition, keeping the new casing.
- `DeleteName` on an unknown name → `ArgumentException`.

**Evaluation**

- A name that refers to a cell behaves like that cell reference. A name that refers to a range behaves like that range.
- Formulas depend on the name itself. Defining, redefining or deleting it recalculates dependents. A formula using an undefined name shows `#NAME?` and **resolves automatically once the name is defined**.

**Interaction with insert and delete**

- Names adjust under §9.5.
- A name whose target is fully deleted gets `refersTo` = `#REF!` and evaluates to `#REF!`.

---

## 10. CLI (`gridline.exe`)

### 10.1 General

- All stdout output is UTF-8 without BOM, with **LF (`\n`) line endings regardless of platform**.
- Errors go to stderr as a single line.
- Addresses are printed in canonical form.

**Exit codes**

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Usage error (unknown command, bad arguments) |
| 2 | File not found or I/O error |
| 3 | Format error (`.gridline` or CSV) |
| 4 | Script finished with at least one `ERR` line |
| 5 | Invalid address |

### 10.2 Commands

| Command | Phase | Behavior |
|---|---|---|
| `gridline eval <file> <addr>…` | 1 | Prints `ADDR\t<canonical value>` for each address, in the order given |
| `gridline eval <file> --all` | 1 | Prints the same line for every used cell, row-major |
| `gridline set <file> <addr> <input> [--out <path>]` | 1 | Sets the cell and saves atomically, to `--out` or in place |
| `gridline script [--file <path>]` | 1 | Runs a script from stdin (§10.3) against a new workbook, or the loaded file |
| `gridline import-csv <csv> <out.gridline> [--delimiter <c>]` | 3 | CSV → `.gridline` |
| `gridline export-csv <file> <out.csv> [--delimiter <c>]` | 3 | `.gridline` → CSV |

### 10.3 Script language

- One command per line. Lines end in LF or CRLF.
- Blank lines and lines starting with `#` are ignored.
- The command word is case-insensitive. Arguments are separated by single spaces.
- On any failure the script prints `ERR\t<message>` and continues. The exit code is 4 if any `ERR` line was printed.
- A command not yet implemented in the current phase prints `ERR\tnot implemented`.

| Command | Phase | Output |
|---|---|---|
| `SET <addr> <input>` | 1 | None. The input is everything after the second space, verbatim. `SET A1` (no input) clears the cell. |
| `SETX <addr> <escaped>` | 1 | Like SET, but the input is unescaped first: `\\`, `\t`, `\n`, `\r` |
| `GET <addr>` | 1 | `ADDR\t<canonical value>` |
| `INPUT <addr>` | 1 | `ADDR\t<input, escaped per §8.3>` |
| `DUMP` | 1 | `ADDR\t<canonical value>` for every used cell, row-major |
| `STATS` | 1 | `EVALUATED\t<LastRecalc.EvaluatedFormulaCount>` |
| `SAVE <path>` | 1 | Atomic save. From Phase 2 on, also calls `MarkSaved()`. |
| `LOAD <path>` | 1 | Replaces the current workbook |
| `UNDO` / `REDO` | 2 | `OK` or `NOOP` |
| `BEGIN` … `END` | 2 | SET and SETX lines between them are applied as one `ApplyBatch` at `END`. Any other command inside the block → `ERR` |
| `PASTE <srcRange> <dstAddr>` | 2 | None |
| `DIRTY` | 2 | `TRUE` or `FALSE` |
| `INSERTROWS <row> <n>` / `DELETEROWS <row> <n>` | 3 | None |
| `INSERTCOLS <col> <n>` / `DELETECOLS <col> <n>` | 3 | None. `col` is given as letters, e.g. `C` |
| `NAME <name> <refersTo>` / `UNNAME <name>` | 3 | None |
| `NAMES` | 3 | `NAME\t<name>\t<refersTo>` per name, sorted |

---

## 11. GUI (`Gridline.App`, Phase 2 unless marked)

### 11.1 Window

- The title is `Gridline - <file name or Untitled>`, with ` *` appended when dirty.
- Default size 1280×800.

### 11.2 Layout

From top to bottom:

1. Menu bar.
2. Name box and formula bar.
3. Grid.
4. Status bar.

### 11.3 Grid

- **Virtualized**: only visible cells are realized.
- Scrolling to row 100,000 and column ZZ MUST stay responsive.
- Column headers show `A…ZZ`; row headers show `1…100000`.
- **Display formatting**:
  - Numbers use `G10` invariant and are right-aligned.
  - Text is left-aligned.
  - Booleans and errors are centered.
- The active cell is highlighted. Shift+arrows or mouse drag selects a range.

### 11.4 Editing

| Input | Action |
|---|---|
| Typing a printable character | Starts editing with that character |
| F2 | Edits the existing input |
| Enter | Commits and moves down |
| Tab | Commits and moves right |
| Esc | Cancels the edit |
| Delete | Clears the selection as one undo step (ApplyBatch) |

- **Name box**: shows the active address. Typing an address and pressing Enter navigates there. An invalid address reverts the box.
- **Formula bar**: shows the active cell's input. Enter commits, Esc reverts.

### 11.5 Menu and shortcuts

- **File**: New (Ctrl+N), Open (Ctrl+O), Save (Ctrl+S), Save As (Ctrl+Shift+S), Import CSV (Phase 3), Export CSV (Phase 3), Exit.
- **Edit**: Undo (Ctrl+Z), Redo (Ctrl+Y), Copy (Ctrl+C), Paste (Ctrl+V).
- **Phase 3**: right-clicking a row or column header offers Insert and Delete for the selected rows or columns.
- **Unsaved changes**: New, Open and Exit on a dirty workbook prompt Save / Don't Save / Cancel.

### 11.6 Clipboard and autosave

**Clipboard**

- Copying within the app followed by pasting uses §9.4 semantics.
- Copy also places the displayed values on the system clipboard as tab-separated text.
- Pasting external text parses it with §8.5 import rules, using Tab as the delimiter, and writes it at the active cell as one ApplyBatch.

**Autosave and recovery**

- While dirty, the app saves atomically every 60 s to `%LOCALAPPDATA%\Gridline\Recovery\<session-guid>.gridline`.
- The recovery file is deleted on a successful Save or a clean exit.
- At startup, if recovery files exist, the app shows a dialog offering to restore each one.

### 11.7 Automation surface (normative for UI tests)

| AutomationId | Element |
|---|---|
| `NameBox` | Name box text field |
| `FormulaBar` | Formula bar text field |
| `Grid` | Grid control |
| `SelectionValue` | Status bar text showing the active cell's canonical value (§8.3) |
| `SelectionSum` | Status bar text showing the sum of numbers in the selection |
| `RecoveryDialog` | Recovery prompt window |
| `MenuFile`, `MenuEdit`, `MenuUndo`, `MenuRedo`, `MenuSave`, `MenuOpen` | Menu items |

---

## 12. Performance budgets

- Measured on the evaluator's reference machine, Release build.
- In-process through the API, after one warm-up iteration, taking the median of 5 runs unless stated otherwise.

| ID | Phase | Scenario | Budget |
|---|---|---|---|
| P-1 | 1 | Load a 100k-cell file (50k numbers, 50k formulas, each with ≤ 3 refs), including the full recalc | ≤ 1.5 s |
| P-2 | 1 | Chain `A2=A1+1 … A100000=A99999+1`; `SetCell("A1", …)` | ≤ 250 ms, no stack overflow, `EvaluatedFormulaCount` = 99,999 |
| P-3 | 1 | `SetCell` on a cell with no dependents in a 100k-cell workbook | median ≤ 1 ms over 1,000 edits |
| P-4 | 1 | `=SUM(A1:A100000)` over 100k numbers; edit one number | ≤ 50 ms |
| P-5 | 1 | Save a 100k-cell workbook | ≤ 1 s |
| P-6 | 2 | GUI: open a 100k-cell file until the grid is rendered | ≤ 3 s |
| P-7 | 1 | 100k numbers in column A plus 1,000 formulas `=SUM($A$1:$A$100000)`; load | ≤ 5 s, process private memory ≤ 600 MB |

---

## 13. Phases and exit criteria

Every phase MUST end with all of the following:

- `dotnet build -c Release` and `dotnet test -c Release` (excluding Category=UI) pass;
- `DECISIONS.md` is updated;
- the work is committed.

**Phase 1 — Engine, file format, CLI**

- §4, §5.1–5.4, §6, §7.1–7.3.
- §8.1–8.4.
- §9.1 Phase 1 members and §9.2.
- §10 Phase 1 commands.
- §12 P-1 through P-5 and P-7.
- §3 architecture tests.

**Phase 2 — Undo, paste, GUI**

- §5.5, §9.3, §9.4.
- §10 Phase 2 commands.
- §11 except items marked Phase 3.
- §12 P-6.

**Phase 3 — Names, structure edits, lookup/date, CSV**

- §7.4–7.6, §8.5, §9.5, §9.6.
- §10 Phase 3 commands.
- §11 Phase 3 items.

Behavior from earlier phases MUST keep working. Regressions are scored.

---

## 14. Testing expectations (your tests)

- Every `src/` project has a test project. Public behavior in §6–§10 is covered by your own tests.
- Use property-based tests (FsCheck) for:
  - undo/redo — a random operation sequence followed by undo-all returns the initial inputs;
  - file round-trip;
  - CSV round-trip.
- The hidden acceptance suite exercises the CLI script mode, the §9 API, byte-exact file output and the §11.7 automation surface. Conform to those sections exactly.