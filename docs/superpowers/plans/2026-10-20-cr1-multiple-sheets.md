# Gridline CR-1 — Multiple Sheets — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement change request CR-1. A workbook gets an ordered list of 1–255 named sheets, qualified references (`Sheet!A1`, `'My Sheet'!A1:B3`), a sheet API, file format v2, per-sheet CSV, CLI/script commands and a GUI tab strip. Every Phase 1–3 behaviour MUST keep working for single-sheet workbooks.

**Architecture:**
- **Sheet identity.** Each sheet has a stable internal `int Id`, allocated from a counter and never reused within one Workbook instance. It also has a mutable `Name` and a position in the order list. Cells live in one `CellStore` per sheet, keyed by the existing `int` cell key. The engine-wide **global key** is `long GKey = ((long)sheetId << 32) | (uint)cellKey`, used by the dependency graph, recalc planner, undo `CellEdit`s and evaluation order. This rules out a sheet index, which changes on Move and Delete.
- **References resolve by name at registration time.**
  - `FormulaDeps` record each reference's sheet name, or null for unqualified.
  - The graph resolves names to ids when a formula is registered, and keeps a `sheet-name → formulas` index.
  - When a sheet name starts or stops existing (Add, Rename, Delete, and their undo/redo), the workbook *rebinds* the formulas indexed under that name. Rebinding means re-resolving and re-registering them, then recalculating them. This is what makes "missing sheet → `#REF!`, resolves once created" work.
- **One mutation path.** `Commit` gains a sheet-layout before/after list (`IReadOnlyList<SheetEntry>`, null = unchanged) next to the existing names before/after. Every sheet operation is a whole diff (cells + names + layout) through `Commit`, so undo, redo, dirty tracking and events come for free. Order within `Commit`:
  1. Install the new layout, creating stores for new ids.
  2. Apply the cell edits.
  3. Install names.
  4. Drop the stores of removed ids.
  5. Rebind the names that changed existence.
  6. Plan and evaluate.

**Tech Stack:** unchanged (.NET 10, C#, xunit, FsCheck, FlaUI, WPF).

**Spec:** `.claude/change request.md` (CR-1) amending `.claude/architecture.md` v1.0. Prerequisite: Phase 3 complete (HEAD `e898ce0`).

## Global Constraints

- All Phase 1–3 Global Constraints still apply (`.superpowers/sdd/global-constraints.md`). Run the full non-UI suite in every task: `dotnet test Gridline.sln -c Release --filter "Category!=UI&Category!=Perf"`. Earlier behaviour MUST NOT regress.
- Sheet count is 1–255. A new workbook has one sheet, `Sheet1`.
- Sheet name rules:
  - 1–31 characters.
  - None of `: \ / ? * [ ]`.
  - Must not begin or end with `'`.
  - Unique case-insensitively (`OrdinalIgnoreCase`).
  - Invalid → `ArgumentException`.
- A name needs quoting iff any of these holds:
  - it contains a character outside `[A-Za-z0-9_.]`;
  - it starts with a digit;
  - it matches `^[A-Za-z]{1,3}[0-9]+$`;
  - it is `TRUE` or `FALSE` (case-insensitive).

  Inside quotes, `''` stands for `'`.
- A range has exactly one sheet prefix. `Sheet2!A1:Sheet2!B3` → `#PARSE!`.
- Canonical printing: the sheet name in its *defined* casing (as written if the sheet doesn't exist), quoted only when required.
- File format v2 is byte-exact:
  - two-space indent;
  - top-level keys in the order `format, version, activeSheet, names, sheets`;
  - sheet keys in the order `name, cells`;
  - sheets in workbook order;
  - LF line endings and a trailing newline (as §8.1).
- Performance: all §12 budgets still hold. P-8: 10 sheets × 10,000 cross-sheet formulas, `RenameSheet` on the most-referenced sheet ≤ 500 ms.
- Exact copy for errors: version > 2 → `GridlineFormatException` whose message contains `unsupported version`. `DeleteSheet` on the last sheet → `InvalidOperationException`.

## Review Focus

1. **A formula that references a sheet before the sheet exists**, then the sheet is added, renamed-to, or restored by undo of its delete. It must go `#REF!` → value → `#REF!` correctly. The hazard is a stale graph registration after undo. Test: `MissingSheet_ResolvesOnAdd_AndUnresolvesOnUndo` (Task 4).
2. **`DeleteSheet` then `Undo` restores verbatim original text.** This covers text like `= data!a1 + 1` on another sheet, names that pointed into the sheet, and the sheet's own position and cells. Test: `DeleteSheet_UndoRestoresVerbatimNamesAndPosition` (Task 5).
3. **Sheet names that need quoting round-trip everywhere.** This covers formulas, API addresses, `GetUsedAddresses`, file save/load, and script args. Examples are `'My Sheet'`, `'It''s'`, `'2024'`, `'A1'` and `'TRUE'`. Tests: `Printer_QuotesOnlyWhenRequired` (Task 2), `V2_RoundTripQuotedNames` (Task 7), `Script_QuotedSheetArgs` (Task 9).
4. **Structural edits are scoped by sheet.** `InsertRows("Data",…)` must not move unqualified references on `Sheet1`, while `Data!A5` on `Sheet1` and unqualified `A5` on `Data` both move. Test: `InsertRows_OnSheet_AdjustsOnlyThatSheetsReferences` (Task 5).
5. **The active sheet disappears through undo/redo.** An example is undoing the `AddSheet` of the active sheet. Exactly one sheet must stay active, with no exception. Test: `UndoAddSheet_WhileActive_ActivatesNeighbour` (Task 4).

---

## File Structure

```
src/Gridline.Core/Addressing/SheetNames.cs        CR1.1/CR1.2 validity, quoting, prefix parsing
src/Gridline.Core/Addressing/QualifiedAddress.cs  "[sheet!]A1" and "[sheet!]A1:B3" API address parsing
src/Gridline.Core/Parsing/{Token,Tokenizer,Ast,Parser,FormulaPrinter,ReferenceRewriter}.cs  (modify) sheet prefixes
src/Gridline.Core/DependencyExtractor.cs           (modify) sheet-aware FormulaDeps
src/Gridline.Core/Dependencies/{DependencyGraph,RecalcPlanner}.cs  (modify) long GKey, sheet resolution, rebind
src/Gridline.Core/GlobalKey.cs                     GKey packing helpers
src/Gridline.Core/Sheet.cs                         internal Sheet (Id, Name, CellStore)
src/Gridline.Core/SheetSnapshot.cs                 public record for multi-sheet FromSnapshot
src/Gridline.Core/Commands/EditStep.cs             (modify) long keys + sheet layout before/after
src/Gridline.Core/Evaluation/{IEvalContext,RangeRef,Arg,Evaluator}.cs  (modify) sheet-aware reads
src/Gridline.Core/Functions/ConditionalFunctions.cs (modify) read via range's sheet
src/Gridline.Core/Addressing/NameRules.cs          (modify) qualified refersTo
src/Gridline.Core/Workbook.cs                      (modify) sheets API; split helpers into partial files if > ~900 lines
src/Gridline.IO/GridlineFile.cs                    (modify) v2 write, v1/v2 read
src/Gridline.IO/CsvFile.cs, CsvOptions.cs          (modify/create) ImportInto, Export(sheet)
src/Gridline.Cli/{CliApp,ScriptRunner,ScriptArgs}.cs  (modify/create) CR1.7
src/Gridline.App/{SheetTabs.cs,MainWindow.*,SheetController.cs,DocumentSession.cs}  CR1.8
```

---

### Task 1: Sheet names and qualified addresses (CR1.1, CR1.2)

**Files:**
- Create: `src/Gridline.Core/Addressing/SheetNames.cs`, `src/Gridline.Core/Addressing/QualifiedAddress.cs`
- Test: `tests/Gridline.Core.Tests/Addressing/SheetNamesTests.cs`

**Interfaces:**
- Produces, in `Gridline.Core.Addressing`:
  - `public static class SheetNames`:
    - `const int MaxLength = 31; const int MaxSheets = 255;`
    - `static bool IsValid(string? name)`: the CR1.1 rules.
    - `static bool NeedsQuoting(string name)`: the CR1.2 four rules.
    - `static string Format(string name)`: quoted with `''` escaping iff `NeedsQuoting`.
    - `internal static bool TryParsePrefix(ReadOnlySpan<char> s, int start, out string name, out int afterBang)`. It reads `'…'!` (with `''`), or an unquoted run of `[A-Za-z0-9_.]` that does *not* need quoting, followed by `!`. On success `afterBang` is the index after `!`. A quoted name may be any non-empty text; validity is checked by callers that need it.
    - `static bool TryParseName(string token, out string name)`. The whole token is one sheet name, either quoted (`'My Sheet'`) or unquoted with `!NeedsQuoting`. Used by the script commands.
  - `public readonly record struct QualifiedAddress(string? Sheet, RangeAddress Range, bool IsRange)`:
    - `static bool TryParse(string? s, out QualifiedAddress a)` accepts `[prefix]A1` or `[prefix]A1:B3`. Prefixes follow `TryParsePrefix`. `Sheet` is null when unqualified. `A1:Sheet!B3` fails.
    - `static bool TryParseCell(string? s, out string? sheet, out CellAddress cell)`.
    - `static bool TryParseColumnOrRow(string s, bool column, out string? sheet, out int index)`. It parses `[prefix]C` (column letters ≤ ZZ) or `[prefix]5` (1..int.MaxValue). Used by the script `INSERTCOLS 'My Data'!C 2`.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData("Sheet1", true)] [InlineData("My Sheet", true)] [InlineData("", false)]
[InlineData("abcdefghijabcdefghijabcdefghij1", true)]   // 31
[InlineData("abcdefghijabcdefghijabcdefghij12", false)] // 32
[InlineData("a:b", false)] [InlineData("a\\b", false)] [InlineData("a/b", false)] [InlineData("a?b", false)]
[InlineData("a*b", false)] [InlineData("a[b", false)] [InlineData("a]b", false)]
[InlineData("'ab", false)] [InlineData("ab'", false)] [InlineData("a'b", true)]
public void IsValid(string name, bool ok) => Assert.Equal(ok, SheetNames.IsValid(name));

[Theory]
[InlineData("Sheet1", "Sheet1")] [InlineData("My Sheet", "'My Sheet'")] [InlineData("2024", "'2024'")]
[InlineData("A1", "'A1'")] [InlineData("xfd99", "'xfd99'")] [InlineData("ABCD1", "ABCD1")]
[InlineData("true", "'true'")] [InlineData("It's", "'It''s'")] [InlineData("_x.y", "_x.y")]
public void Format_QuotesOnlyWhenRequired(string name, string expected) => Assert.Equal(expected, SheetNames.Format(name));

[Fact] public void QualifiedAddress_Parses()
{
    Assert.True(QualifiedAddress.TryParse("'It''s'!b2:a1", out var q));
    Assert.Equal("It's", q.Sheet); Assert.Equal("A1:B2", q.Range.ToString()); Assert.True(q.IsRange);
    Assert.True(QualifiedAddress.TryParse("a1", out q)); Assert.Null(q.Sheet);
    Assert.False(QualifiedAddress.TryParse("Sheet2!A1:Sheet2!B3", out _));
    Assert.False(QualifiedAddress.TryParse("My Sheet!A1", out _));   // needs quotes
    Assert.False(QualifiedAddress.TryParse("A1!B2", out _));         // looks like a ref → needs quotes
    Assert.False(QualifiedAddress.TryParse("''!A1", out _));         // empty name
}
```

- [ ] **Step 2:** Run `dotnet test tests/Gridline.Core.Tests -c Release --filter FullyQualifiedName~SheetNamesTests`. Expected: FAIL (types missing).
- [ ] **Step 3:** Implement both files. Ordinal/invariant only. `NeedsQuoting("")` is true.
- [ ] **Step 4:** Run the same filter, then the full non-UI suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): sheet name rules and qualified addresses (CR1.1, CR1.2)`.

---

### Task 2: Sheet prefixes in the formula language (CR1.2, §5.5)

**Files:**
- Modify: `src/Gridline.Core/Parsing/{Token,Tokenizer,Ast,Parser,FormulaPrinter,ReferenceRewriter}.cs`, plus Workbook call sites (compile only)
- Test: `tests/Gridline.Core.Tests/Parsing/SheetRefParsingTests.cs`

**Interfaces:**
- Consumes: `SheetNames.TryParsePrefix`, `SheetNames.Format`.
- Produces:
  - Token kind `TokenKind.SheetPrefix`, whose `Text` is the unescaped sheet name.
  - The tokenizer tries `TryParsePrefix` before the number, word and `'` branches. A prefix must be *immediately* followed by a cell reference token. Otherwise the tokenizer fails, which makes the formula `#PARSE!` (whitespace after `!` is not allowed). Unquoted prefixes may start with a letter, `_` or `.`.
  - AST:
    - `RefNode(int Row, int Col, bool RowAbs, bool ColAbs, string? Sheet = null)`.
    - `RangeNode(RefNode First, RefNode Second, string? Sheet = null)`. The inner refs always have `Sheet == null`; the range's sheet is on the `RangeNode`.
  - Parser: `SheetPrefix CellRef [":" CellRef]`. A second prefix after `:` is a syntax error.
  - `FormulaPrinter.Print(Node root, Func<string,string?> nameCasing, Func<string,string?>? sheetCasing = null)`. `sheetCasing` receives the upper-cased sheet name and returns its defined casing, or null to keep it as written. Output is `SheetNames.Format(casing) + "!"` before the ref or range. Ranges print one prefix.
  - `ReferenceRewriter`:
    - `Shift` keeps `Sheet` untouched.
    - `Structural(Node root, StructuralEdit e, Func<string?, bool> applies, out bool changed)`. A ref or range is adjusted only when `applies(node.Sheet)` is true. The existing overload stays as `applies = _ => true`.
    - New `RenameSheet(Node root, string oldName, string? newName, out bool changed)`. Refs and ranges whose `Sheet` equals `oldName` (`OrdinalIgnoreCase`) get `Sheet = newName`. When `newName` is null they become `ErrorNode("#REF!")`.

- [ ] **Step 1: Write failing tests** (through `Parser.Parse` + `FormulaPrinter.Print`, internal-visible):

```csharp
[Theory]
[InlineData("Sheet2!a1", "=Sheet2!A1")]
[InlineData("'my data'!$A$1:b3+1", "='my data'!$A$1:B3+1")]
[InlineData("'It''s'!A1", "='It''s'!A1")]
[InlineData("SUM(Data!A1:A3, A1)", "=SUM(Data!A1:A3,A1)")]
[InlineData("'Sheet1'!A1", "=Sheet1!A1")]          // quotes dropped when not required
[InlineData("_x!A1", "=_x!A1")]
public void Printer_QuotesOnlyWhenRequired(string body, string printed) =>
    Assert.Equal(printed, FormulaPrinter.Print(Parse(body).Root!, _ => null));

[Theory]
[InlineData("Sheet2!A1:Sheet2!B3")] [InlineData("Sheet2! A1")] [InlineData("My Sheet!A1")]
[InlineData("2024!A1")] [InlineData("A1!B2")] [InlineData("Sheet2!Rate")] [InlineData("''!A1")]
public void Invalid_IsParseError(string body) => Assert.True(Parse(body).IsParseError);

[Fact] public void Printer_UsesDefinedSheetCasing() =>
    Assert.Equal("=Data!A1", FormulaPrinter.Print(Parse("DATA!a1").Root!, _ => null, s => s == "DATA" ? "Data" : null));

[Fact] public void RenameSheet_RewritesAndRefs()
{
    var r = ReferenceRewriter.RenameSheet(Parse("data!A1+Other!B1+A1").Root!, "Data", "New Data", out bool ch);
    Assert.True(ch); Assert.Equal("='New Data'!A1+Other!B1+A1", FormulaPrinter.Print(r, _ => null));
    r = ReferenceRewriter.RenameSheet(Parse("SUM(Data!A1:B2)").Root!, "data", null, out _);
    Assert.Equal("=SUM(#REF!)", FormulaPrinter.Print(r, _ => null));
}

[Fact] public void Structural_RespectsSheetPredicate()
{
    var e = new StructuralEdit(Axis.Row, 1, 1, IsInsert: true);
    var r = ReferenceRewriter.Structural(Parse("A1+Data!A1").Root!, e, s => s is not null, out _);
    Assert.Equal("=A1+Data!A2", FormulaPrinter.Print(r, _ => null));
}
```

- [ ] **Step 2:** Run `--filter FullyQualifiedName~SheetRefParsingTests`. Expected: FAIL.
- [ ] **Step 3:** Implement. `DependencyExtractor` and `Evaluator` keep compiling unchanged (they ignore `Sheet` until Task 3/4). Existing `Parsing/*Tests` must stay green; the printer output for unqualified formulas is byte-identical.
- [ ] **Step 4:** Run the filter, then the full non-UI suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): sheet-qualified references in parser, printer and rewriter (CR1.2, §5.5)`. Record in DECISIONS.md:
  - no whitespace between `!` and the reference;
  - prefixes only before cell references, never before names.

---

### Task 3: Sheet-aware dependency graph and planner (CR1.2, §6.1, §6.7)

**Files:**
- Create: `src/Gridline.Core/GlobalKey.cs`
- Modify: `src/Gridline.Core/Dependencies/{DependencyGraph,RecalcPlanner}.cs`, `src/Gridline.Core/DependencyExtractor.cs`, and `Workbook.cs` (switch to `long` keys, still one sheet with id 0, name `Sheet1`)
- Test: `tests/Gridline.Core.Tests/Dependencies/SheetGraphTests.cs`, plus existing `DependencyGraphTests` updated for `long`

**Interfaces:**
- Produces:
  - `internal static class GlobalKey`:
    - `long Of(int sheetId, int cellKey)`;
    - `int Sheet(long g)`;
    - `int Cell(long g)`;
    - `CellAddress Address(long g)`.

    Row-major order within a sheet is preserved: `GKey` ordering is (sheetId, cellKey).
  - `FormulaDeps(IReadOnlyList<SheetCell> Cells, IReadOnlyList<SheetRange> Ranges, IReadOnlyList<string> Names)`:
    - `internal readonly record struct SheetCell(string? Sheet, int Key)`;
    - `internal readonly record struct SheetRange(string? Sheet, RangeAddress Range)`;
    - `Sheet` is null for unqualified refs.
  - `DependencyGraph`, keys `long`; name node ids are negative `long`:
    - `void SetDependencies(long formulaKey, FormulaDeps? deps, Func<string, int?> resolveSheet)`:
      - Unqualified refs resolve to `GlobalKey.Sheet(formulaKey)`.
      - An unresolved qualified ref registers nothing in the cell or range index.
      - Every qualified sheet name (upper-cased, resolved or not) is added to the sheet-reference index. The graph stores the resolved registrations so that unregistering never re-resolves.
    - `void SetNameTarget(string upperName, int? sheetId, RangeAddress? target)`.
    - `IEnumerable<long> FormulasReferencingSheet(string upperSheetName)`.
    - `IReadOnlyList<long> Rebind(string upperSheetName, Func<string, int?> resolveSheet)`. It re-registers every formula in that index entry from its stored deps and returns them.
    - `DirectDependents(long)` and `DistinctDirectDependentsOfMany(IEnumerable<long>)`. The range index is per sheet (`Dictionary<int, ColumnIndex?[]>`).
  - `RecalcPlanner.Plan(IEnumerable<long> changedKeys, IEnumerable<string> changedNames, IEnumerable<long> extraRoots, Func<long,bool> isFormula)`, `PlanAll(IEnumerable<long>)`, `RecalcPlan(IReadOnlyList<long> Order, IReadOnlySet<long> CycleMembers)`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void QualifiedRef_ResolvedAtRegistration_AndRebound()
{
    var g = new DependencyGraph();
    var sheets = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase) { ["Sheet1"] = 0 };
    int? Resolve(string n) => sheets.TryGetValue(n, out int id) ? id : null;
    long f = GlobalKey.Of(0, CellAddress.Parse("B1").Key);
    g.SetDependencies(f, Deps(cells: [new("Data", CellAddress.Parse("A1").Key)]), Resolve);
    long dataA1 = GlobalKey.Of(7, CellAddress.Parse("A1").Key);
    Assert.Empty(g.DirectDependents(dataA1));          // Data missing: not registered
    Assert.Equal([f], g.FormulasReferencingSheet("DATA"));
    sheets["Data"] = 7;
    Assert.Equal([f], g.Rebind("DATA", Resolve));
    Assert.Equal([f], g.DirectDependents(dataA1));
    sheets.Remove("Data");
    g.Rebind("DATA", Resolve);
    Assert.Empty(g.DirectDependents(dataA1));
}

[Fact] public void Unqualified_ResolvesToOwnSheet()  // same cell key on sheet 0 vs 1 are distinct
[Fact] public void RangeIndex_IsPerSheet()          // Data!A1:A10 does not match Sheet1!A5
[Fact] public void Planner_CrossSheetCycle_AllMembersFlagged() // Sheet1!A1→Data!A1→Sheet1!A1
```

- [ ] **Step 2:** Run `--filter FullyQualifiedName~SheetGraphTests`. Expected: FAIL.
- [ ] **Step 3:** Implement and convert the Workbook to `long` keys throughout:
  - `CellEdit(long Key, …)`;
  - `Commit`, `Evaluate`, `_isFormula`;
  - a single `Sheet` of id 0.

  `Raise` still prints unqualified addresses. `resolveSheet` resolves only `Sheet1` for now.
- [ ] **Step 4:** Run the full non-UI suite, then `dotnet test Gridline.sln -c Release --filter Category=Perf`. Expected: PASS, P-1–P-7 within budget (the `long` key must not regress them).
- [ ] **Step 5:** Commit `refactor(core): global sheet-qualified keys in dependency graph and planner (CR1.2, §6.1, §6.7)`.

---

### Task 4: Workbook sheets, cross-sheet evaluation, qualified API, names (CR1.1–CR1.4)

**Files:**
- Create: `src/Gridline.Core/Sheet.cs`
- Modify: `Workbook.cs` (consider `Workbook.Sheets.cs` partial), `Commands/EditStep.cs`, `Evaluation/{IEvalContext,RangeRef,Arg,Evaluator}.cs`, `Functions/ConditionalFunctions.cs`, `Addressing/NameRules.cs`
- Test: `tests/Gridline.Core.Tests/SheetsTests.cs`, `tests/Gridline.Core.Tests/CrossSheetEvalTests.cs`; update `NamesTests.cs` for qualified `GetNames`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces (public on `Workbook`):
  - `IReadOnlyList<string> SheetNames { get; }`;
  - `string ActiveSheet { get; set; }`: unknown → `ArgumentException`; not undoable; no step; not dirty.
  - `void AddSheet(string name, int? index = null)`. Errors:
    - invalid or duplicate name → `ArgumentException`;
    - index outside `0..Count` → `ArgumentOutOfRangeException`;
    - a 256th sheet → `InvalidOperationException`.
  - `void MoveSheet(string name, int newIndex)` with `newIndex` in `0..Count-1`.
  - `IReadOnlyList<string> GetUsedAddresses(string sheet)`: unqualified, row-major, for that sheet.
  - `event EventHandler? SheetsChanged`. It is raised after any change of the sheet list, sheet order, sheet names or active sheet, including through undo/redo.
  - `RenameSheet`/`DeleteSheet` are declared here too but implemented in Task 5.
- Internal:
  - `internal readonly record struct SheetEntry(int Id, string Name)`;
  - `EditStep(long Id, IReadOnlyList<CellEdit> Cells, names before/after, IReadOnlyList<SheetEntry>? SheetsBefore, IReadOnlyList<SheetEntry>? SheetsAfter)`;
  - `Commit(edits, newNames, newSheets, record)`, following the order in the Architecture block.
- `IEvalContext` changes. The 2-argument forms are removed, so every call site must pick a sheet:
  - `int FormulaSheet { get; }`;
  - `CellValue GetCellValue(int sheet, int row, int col)`;
  - `EnumerateNonBlank(int sheet, RangeAddress)`;
  - `bool TryResolveSheet(string name, out int sheet)`;
  - `TryResolveName(string, out int sheet, out RangeAddress, out bool isRange)`.
- `RangeRef(int Sheet, RangeAddress Address, IEvalContext Ctx)`.
- Evaluation of a missing sheet:
  - A ref or range to a missing sheet evaluates to `#REF!`.
  - In `Arg` it is not a reference, and its value is `#REF!`.
- `NameRules`:
  - `TryParseRefersTo(string s, out string? sheet, out RangeAddress target)`. The prefix is optional; the `$` rules are unchanged.
  - `FormatRefersTo(string sheet, RangeAddress t, bool asRange)` gives `Data!$B$1`, `'My Sheet'!$A$1:$A$10`.

Rules to implement (from CR1.1–1.4):
- **Address parameters.** Every address parameter (`SetCell`, `GetInput`, `GetValue`, `ApplyBatch`, `CopyPaste`) accepts `QualifiedAddress` syntax.
  - Unqualified means the active sheet.
  - An unknown sheet → `ArgumentException`.
- **`GetUsedAddresses()`.**
  - With 1 sheet: unqualified, unchanged.
  - With more: `SheetNames.Format(name) + "!" + addr` for all sheets, in sheet order, then row-major.
  - `CellsChanged` addresses follow the same rule, using the sheet count after the commit (record in DECISIONS.md).
- **Unqualified references in a formula** resolve to the formula's own sheet. A qualified reference resolves case-insensitively.
- **Names.**
  - Defining an unqualified `refersTo` qualifies it with the active sheet.
  - A `refersTo` naming a non-existent sheet → `ArgumentException` (decision: names never dangle, record it).
  - `GetNames()` returns qualified forms in defined sheet casing, e.g. `Sheet1!$B$1`. This also applies to single-sheet workbooks, as CR1.4 overrides §9.6 output; update the existing tests and record it.
  - The legacy `FromSnapshot(cells, names)` builds one sheet `Sheet1` and accepts unqualified or qualified `refersTo`.
- **Active sheet after a layout change.** When undo/redo/delete removes the active sheet, the sheet now at the old active index becomes active, else the last sheet. A rename keeps the active sheet (tracked by id).
- **Sheet existence changes.** After installing a new layout, `Rebind` every upper-cased name that was added or removed, and add the rebound formulas as `extraRoots`.

- [ ] **Step 1: Write failing tests** (examples; write each as its own `[Fact]`):

```csharp
[Fact] public void NewWorkbook_HasSheet1Active()
{ var wb = new Workbook(); Assert.Equal(["Sheet1"], wb.SheetNames); Assert.Equal("Sheet1", wb.ActiveSheet); }

[Fact] public void CrossSheet_RefAndRange()
{
    var wb = new Workbook(); wb.AddSheet("Data");
    wb.SetCell("Data!A1", "2"); wb.SetCell("data!A2", "3");
    wb.SetCell("A1", "=Data!A1*10"); wb.SetCell("A2", "=SUM(Data!A1:A2)"); wb.SetCell("A3", "=SUMIF(Data!A1:A2,\">2\",Data!A1:A2)");
    Assert.Equal("N:20", V(wb, "A1")); Assert.Equal("N:5", V(wb, "A2")); Assert.Equal("N:3", V(wb, "A3"));
    wb.SetCell("Data!A1", "4"); Assert.Equal("N:40", V(wb, "Sheet1!A1"));
}

[Fact] public void MissingSheet_ResolvesOnAdd_AndUnresolvesOnUndo()
{
    var wb = new Workbook(); wb.SetCell("A1", "=New!B1+1");
    Assert.Equal("E:#REF!", V(wb, "A1"));
    wb.AddSheet("New"); Assert.Equal("N:1", V(wb, "A1"));
    wb.SetCell("New!B1", "5"); Assert.Equal("N:6", V(wb, "A1"));
    wb.Undo(); wb.Undo(); Assert.Equal("E:#REF!", V(wb, "A1"));
    wb.Redo(); wb.Redo(); Assert.Equal("N:6", V(wb, "A1"));
}

[Fact] public void UnqualifiedRef_IsFormulasOwnSheet()  // Data!B1 "=A1" reads Data!A1 even when Sheet1 is active
[Fact] public void CrossSheetCycle_IsCirc()           // Sheet1!A1 "=Data!A1", Data!A1 "=Sheet1!A1" → both E:#CIRC!
[Fact] public void AddSheet_Validation()              // invalid name, dup "sheet1", index 3 of 2, 256th sheet
[Fact] public void AddMove_AreOneUndoStepEach_ActiveIsNot() // CanUndo / IsDirty after ActiveSheet set stays false
[Fact] public void UndoAddSheet_WhileActive_ActivatesNeighbour()
[Fact] public void GetUsedAddresses_SingleVsMulti()   // ["A1"] → after AddSheet("My Data") + set: ["Sheet1!A1","'My Data'!B2"]
[Fact] public void Names_QualifiedWithActiveSheet()   // ActiveSheet="Data"; DefineName("Rate","$B$1") → GetNames ("Rate","Data!$B$1"); =Rate on Sheet1 reads Data!B1
[Fact] public void Names_UnknownSheet_Throws()
[Fact] public void SheetsChanged_RaisedForAddMoveActivateUndo()
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~SheetsTests|FullyQualifiedName~CrossSheetEvalTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement. Keep every existing unqualified overload behaving exactly as before on the active sheet. Existing `InsertRows(int,int)` etc. act on the active sheet; the sheet-scoped versions arrive in Task 5.
- [ ] **Step 4:** Run the full non-UI suite. Expected: PASS. The only expected test edits are `GetNames`/`NAMES` values gaining the `Sheet1!` prefix.
- [ ] **Step 5:** Extend `UndoProperties` (FsCheck) so its operation generator includes `AddSheet`, `MoveSheet`, and `SetCell` on a random existing sheet. Undo-all must restore the initial inputs *and* `SheetNames`. Run it and expect PASS.
- [ ] **Step 6:** Commit `feat(core): multiple sheets, qualified addresses and names (CR1.1-CR1.4)`. Record the decisions in DECISIONS.md.

---

### Task 5: Rename, delete, sheet-scoped structure edits, cross-sheet paste (CR1.3)

**Files:**
- Modify: `Workbook.cs` (or its `Workbook.Sheets.cs` partial)
- Test: `tests/Gridline.Core.Tests/SheetEditTests.cs`

**Interfaces:**
- Consumes:
  - `ReferenceRewriter.RenameSheet` and `ReferenceRewriter.Structural(…, applies, …)` (Task 2);
  - `DependencyGraph.FormulasReferencingSheet` (Task 3);
  - `Commit` with layout (Task 4).
- Produces:
  - `void RenameSheet(string oldName, string newName)`:
    - unknown old name, or invalid/duplicate new name → `ArgumentException`;
    - a case-only rename of the same sheet is allowed;
    - an identical name is a no-op with no step.
  - `void DeleteSheet(string name)`: the last sheet → `InvalidOperationException`.
  - `void InsertRows(string sheet, int row, int count)`, plus `DeleteRows`, `InsertColumns` and `DeleteColumns` with the same shape. The existing `(int,int)` overloads call these with `ActiveSheet`.

Rules:
- **Rename.** Rewrite every formula from `FormulasReferencingSheet(upper(old))` with `RenameSheet(root, old, new)`. Print each with the sheet casing of the *new* layout, and rewrite names' `refersTo`. The result is one `Commit`: cells + names + layout. Rewrites whose length exceeds `MaxInputLength` → `InvalidOperationException`, with no change.
- **Delete.** One `Commit` containing:
  - cleared cells of the sheet;
  - formulas elsewhere rewritten with `RenameSheet(root, name, null)`, whose `#REF!` is re-emitted;
  - names into the sheet set to `#REF!`;
  - the new layout.

  Undo restores the cells, the verbatim original texts (they are the `OldInput`s), the names and the position.
- **Sheet-scoped structural edit on T.**
  - Cells move only on T.
  - Formulas on T rewrite with `applies = s => s is null || resolve(s) == T`.
  - Formulas on other sheets rewrite with `applies = s => s is not null && resolve(s) == T`, visiting only `FormulasReferencingSheet(upper(name of T))`.
  - Names whose sheet is T shift.
  - The overflow checks (`InsertWouldOverflow`) look at T only.
- **CopyPaste across sheets.** The source and destination prefixes are independent, and an unqualified one means the active sheet. The `Shift` rewrite keeps each ref's own `Sheet`, so unqualified refs follow the destination and qualified refs keep their sheet.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void RenameSheet_RewritesFormulasAndNames_UndoVerbatim()
{
    var wb = new Workbook(); wb.AddSheet("Data");
    wb.SetCell("A1", "= data!a1 + 1"); wb.SetCell("Data!A1", "1"); wb.DefineName("Rate", "Data!$B$1");
    wb.RenameSheet("Data", "My Data");
    Assert.Equal("='My Data'!A1+1", wb.GetInput("A1"));
    Assert.Equal(new KeyValuePair<string,string>("Rate", "'My Data'!$B$1"), wb.GetNames()[0]);
    Assert.Equal("N:2", V(wb, "A1"));
    wb.Undo(); Assert.Equal("= data!a1 + 1", wb.GetInput("A1")); Assert.Equal(["Sheet1", "Data"], wb.SheetNames);
}

[Fact] public void RenameSheet_ToDanglingName_ResolvesThoseRefs() // "=Later!A1" becomes live after RenameSheet("Data","Later")

[Fact] public void DeleteSheet_UndoRestoresVerbatimNamesAndPosition()
{
    var wb = new Workbook(); wb.AddSheet("Data", 0); wb.SetCell("Data!A1", "7");
    wb.SetCell("A1", "= data!a1 + 1"); wb.DefineName("Rate", "Data!$A$1");
    wb.DeleteSheet("Data");
    Assert.Equal("=#REF!+1", wb.GetInput("A1")); Assert.Equal("#REF!", wb.GetNames()[0].Value);
    Assert.Equal("E:#REF!", V(wb, "A1"));
    wb.Undo();
    Assert.Equal(["Data", "Sheet1"], wb.SheetNames); Assert.Equal("= data!a1 + 1", wb.GetInput("Sheet1!A1"));
    Assert.Equal("Data!$A$1", wb.GetNames()[0].Value); Assert.Equal("N:8", V(wb, "Sheet1!A1"));
}

[Fact] public void DeleteLastSheet_Throws()            // InvalidOperationException, nothing changes
[Fact] public void DeleteActiveSheet_ActivatesNeighbour()

[Fact] public void InsertRows_OnSheet_AdjustsOnlyThatSheetsReferences()
{
    var wb = new Workbook(); wb.AddSheet("Data");
    wb.SetCell("A1", "=A5+Data!A5"); wb.SetCell("Data!B1", "=A5+Sheet1!A5");
    wb.InsertRows("Data", 2, 3);
    Assert.Equal("=A5+Data!A8", wb.GetInput("A1"));
    Assert.Equal("=A8+Sheet1!A5", wb.GetInput("Data!B1"));
}

[Fact] public void CopyPaste_AcrossSheets_UnqualifiedFollowDestination()
{
    var wb = new Workbook(); wb.AddSheet("Data");
    wb.SetCell("A1", "=B1+Data!B1");
    wb.CopyPaste("Sheet1!A1", "Data!A2");
    Assert.Equal("=B2+Data!B2", wb.GetInput("Data!A2"));
}
```

- [ ] **Step 2:** Run `--filter FullyQualifiedName~SheetEditTests`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Add `RenameSheet`, `DeleteSheet` and sheet-scoped `InsertRows`/`DeleteRows` to the `UndoProperties` generator. Run the full non-UI suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): rename/delete sheets, sheet-scoped structure edits, cross-sheet paste (CR1.3)`.

---

### Task 6: P-8 and the performance regression check (CR1.9)

**Files:**
- Modify: `tests/Gridline.Core.Tests/Performance/PerfTests.cs`, and whatever Core code the measurement shows needs it
- Test: `P8_RenameMostReferencedSheet`

**Workload:**
- Sheets `S1`…`S10`. Each `Si` has numbers in `A1:A10000` and, in `B1:B10000`, formulas `=S{j}!A{r}+S1!A{r}` with `j = i % 10 + 1`. So `S1` is the most referenced (all 100k formulas; 10k twice).
- Build with `FromSnapshot` (Task 7 adds the multi-sheet overload). If Task 7 has not landed, build through the API with `ApplyBatch` per sheet.
- Measure `RenameSheet("S1","Renamed")` / `RenameSheet("Renamed","S1")` alternately with `PerfHarness.Median`.

- [ ] **Step 1:** Write `P8_RenameMostReferencedSheet` with these assertions:
  - median ≤ 500 ms;
  - afterwards `GetInput("S2!B1") == "=S3!A1+Renamed!A1"` (or `S1` after an even number of renames);
  - `GetValue` unchanged.
- [ ] **Step 2:** Run `dotnet test tests/Gridline.Core.Tests -c Release --filter "Category=Perf"`. Record the timing.
- [ ] **Step 3:** If P-8 is over budget, optimise. Permitted (record any you use in DECISIONS.md):
  - (a) build the rewritten `Cell` directly from the rewritten AST, skipping re-parse, with `DependencyExtractor.Extract` on that AST;
  - (b) a rename-rewritten formula keeps its value and is not a plan root, because a rename never changes what a reference points at. Only `Rebind` results of the new name are roots.
  - (c) re-register graph deps only for formulas whose resolved ids changed. For (c), the stored sheet-name strings still must be updated.
- [ ] **Step 4:** Run the full Perf category in Core and IO. Expected: P-1–P-5, P-7 and P-8 all within budget.
- [ ] **Step 5:** Commit `perf(core): P-8 rename across sheets (CR1.9, §12)`.

---

### Task 7: File format version 2 (CR1.5)

**Files:**
- Create: `src/Gridline.Core/SheetSnapshot.cs`
- Modify: `Workbook.cs` (multi-sheet `FromSnapshot`), `src/Gridline.IO/GridlineFile.cs`
- Test: `tests/Gridline.IO.Tests/GridlineFileV2Tests.cs`, plus updated `GridlineFileTests.cs`/`RoundTripProperties.cs` and any CLI tests that compare saved bytes (expected text becomes v2)

**Interfaces:**
- Produces:
  - `public sealed record SheetSnapshot(string Name, IReadOnlyList<KeyValuePair<string,string>> Cells)` in `Gridline.Core`. Its cell addresses are unqualified.
  - `public static Workbook FromSnapshot(IReadOnlyList<SheetSnapshot> sheets, IReadOnlyList<KeyValuePair<string,string>> names, string activeSheet)`. Zero sheets, an invalid or duplicate sheet name, an unknown `activeSheet`, or anything the v1 overload rejects → `ArgumentException`.
  - `GridlineFile.Serialize` always writes v2.
  - `GridlineFile.Load` reads v1 (migration) and v2.

Exact v2 text for the CR example. The empty forms `"names": {}` and `"cells": []` follow §8.1's existing style:

```
{
  "format": "gridline",
  "version": 2,
  "activeSheet": "Sheet1",
  "names": {
    "Rate": "Data!$B$1"
  },
  "sheets": [
    {
      "name": "Sheet1",
      "cells": [
        ["A1", "=Data!B2*Rate"]
      ]
    },
    {
      "name": "Data",
      "cells": []
    }
  ]
}
```

Loading rules:
- **v1** → one sheet `Sheet1` (active). Each name `$X$n` becomes `Sheet1!$X$n`, `#REF!` stays, and formula texts are unchanged.
- **v2:**
  - `sheets` is required and must be a non-empty array of objects with string `name` and array `cells`;
  - `activeSheet` is a required string;
  - violations, plus a duplicate or invalid sheet name, zero sheets, or an unknown `activeSheet` → `GridlineFormatException`.
- **Version > 2** → `"unsupported version " + n` (existing message).
- **Version 0 or negative** → the existing `invalid version` message.

- [ ] **Step 1: Write failing tests:**
  - `V2_SerializeMatchesCanonicalExample`: byte-exact against the block above.
  - `V1_LoadMigratesNames`: `{"format":"gridline","version":1,"names":{"Rate":"$B$1"},"cells":[["A1","=Rate"]]}` → `SheetNames == ["Sheet1"]`, `GetNames()[0].Value == "Sheet1!$B$1"`, input unchanged, value computed. Re-serialising yields version 2.
  - `V3_Unsupported`: message contains `unsupported version`.
  - `V2_Invalid_Throws`, a theory over duplicate (case-insensitive) names, invalid name `a*b`, `"sheets": []`, `activeSheet` unknown or missing.
  - `V2_RoundTripQuotedNames`: sheets `My Sheet`, `It's`, `2024` with cross-references. Save → load → save is byte-identical, and the values are equal.
  - `V2_ActiveSheetPreserved`.
- [ ] **Step 2:** Run `--filter FullyQualifiedName~GridlineFileV2Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement. Extend the FsCheck file round-trip generator to 1–4 sheets with generated valid names.
- [ ] **Step 4:** Run the full non-UI suite and the IO Perf tests (P-1, P-5, P-7 load/save single-sheet v2). Expected: PASS.
- [ ] **Step 5:** Commit `feat(io): gridline file format version 2 (CR1.5, §8.1)`.

---

### Task 8: CSV per sheet (CR1.6)

**Files:**
- Create: `src/Gridline.IO/CsvOptions.cs`
- Modify: `src/Gridline.IO/CsvFile.cs`, `Workbook.cs` (one public overload)
- Test: `tests/Gridline.IO.Tests/CsvSheetTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record CsvOptions(char Delimiter = ',')`.
  - `public static void CsvFile.ImportInto(Workbook wb, string sheetName, Stream source, CsvOptions? options = null)`:
    - It reads the stream fully, then reuses `Decode` and `DelimitedReader`.
    - A bad delimiter, or an invalid or existing sheet name → `ArgumentException`.
    - A malformed CSV → `GridlineFormatException` with nothing changed.
    - It appends the sheet as one undo step.
  - `public void Workbook.AddSheet(string name, int? index, IReadOnlyList<KeyValuePair<string,string>> cells)`: adds the sheet with unqualified cell inputs as a single step. `AddSheet(name, index)` delegates with an empty list.
  - `CsvFile.Export(Workbook wb, char delimiter = ',', string? sheet = null)` and `ExportToPath(…, string? sheet = null)`. `null` means the active sheet; an unknown sheet → `ArgumentException`.
  - `CsvFile.Import` is unchanged and creates `Sheet1`.

DECISIONS.md: the CR's `Csv.*` names map to the existing `CsvFile` class.

- [ ] **Step 1: Write failing tests:**
  - `ImportInto_AppendsSheet_OneUndoStep`: `"1,=x\n"` → `SheetNames == ["Sheet1","Data"]`, `GetInput("Data!B1") == "'=x"`, one `Undo` removes it.
  - `ImportInto_ExistingName_Throws`, case-insensitive `"sheet1"`.
  - `ImportInto_Malformed_ChangesNothing`.
  - `Export_SheetParameter`: export `"Data"` vs the default active sheet.
  - `Export_UnknownSheet_Throws`.
- [ ] **Step 2:** Run `--filter FullyQualifiedName~CsvSheetTests`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the full non-UI suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(io): CSV import into and export from a sheet (CR1.6, §8.5)`.

---

### Task 9: CLI and script (CR1.7)

**Files:**
- Create: `src/Gridline.Cli/ScriptArgs.cs`
- Modify: `src/Gridline.Cli/{CliApp,ScriptRunner}.cs`
- Test: `tests/Gridline.Cli.Tests/ScriptSheetTests.cs`, `tests/Gridline.Cli.Tests/CliSheetTests.cs`

**Interfaces:**
- Consumes: `QualifiedAddress`, `SheetNames.TryParseName/Format`, the Workbook sheet API, `CsvFile.ImportInto/Export(sheet)`.
- Produces:
  - `internal static class ScriptArgs { static bool TrySplit(string rest, out List<string> args); }`. It splits on single spaces outside `'…'`, where `''` inside quotes is part of the token and an unterminated quote fails.
  - `internal string Workbook.CanonicalAddress(string address)`, or a CLI helper. It echoes a cell address canonically, keeping the prefix only if one was given, as `SheetNames.Format(definedCasing) + "!" + A1`. An unknown sheet → `ArgumentException`.

Behaviour:
- **`GET`, `INPUT`, `SET`, `SETX`, `PASTE`** accept qualified addresses and echo per the rule above. `GET data!a1` prints `Data!A1\t…`. Inside `BEGIN…END` the qualified text is buffered and validated at `END`, as today.
- **`eval`** accepts qualified addresses. Syntax is checked before load (exit 5). An unknown sheet after load is also exit 5. `--all` follows the `GetUsedAddresses` rule. `set` accepts a qualified address the same way.
- **`DUMP`** uses `GetUsedAddresses()`.
- **New commands.** Each argument is a sheet name per `TryParseName`; usage errors print `ERR\tusage: …`.
  - `ADDSHEET <name> [index]`
  - `RENAMESHEET <old> <new>`
  - `DELETESHEET <name>`
  - `MOVESHEET <name> <index>`
  - `ACTIVATE <name>`
  - `SHEETS` prints `SHEET\t<i>\t<Escaping.Escape(name)>\n` per sheet (0-based), then `ACTIVE\t<name>\n`.
- **`INSERTROWS`/`DELETEROWS`/`INSERTCOLS`/`DELETECOLS`** accept `[<sheet>!]<row|col> <n>` via `QualifiedAddress.TryParseColumnOrRow`.
- **`import-csv … --sheet <name>`**:
  - if `<out>` exists, load it and `ImportInto` a new sheet `<name>`;
  - otherwise write a new workbook whose single sheet is `<name>`, built via `Import`, then `RenameSheet` + `MarkSaved` is fine.
  - An invalid or duplicate name → exit 1.
- **`export-csv … --sheet <name>`** exports that sheet. An unknown sheet → exit 1.
- `--sheet` twice, or `--sheet` without a value → exit 1. Record all of this in DECISIONS.md.

- [ ] **Step 1: Write failing tests:**
  - `Script_SheetCommands`: run

    ```
    ADDSHEET 'My Data'
    ADDSHEET Zed 0
    SHEETS
    ACTIVATE 'my data'
    SHEETS
    ```

    Expect `SHEET\t0\tZed\nSHEET\t1\tSheet1\nSHEET\t2\tMy Data\nACTIVE\tSheet1\n…ACTIVE\tMy Data\n`.
  - `Script_QualifiedGetSet`: `SET 'My Data'!a1 5`, `SET A1 ='my data'!A1*2`, `GET 'MY DATA'!A1` → `'My Data'!A1\tN:5`, `GET A1` → `A1\tN:10`.
  - `Script_QuotedSheetArgs`:
    - `RENAMESHEET 'My Data' 'It''s'` then `SHEETS` contains `It's`;
    - `INSERTCOLS 'It''s'!A 2` shifts `A1`'s formula to `'It''s'!C1`;
    - `ADDSHEET 'bad*'` → `ERR`.
  - `Script_Dump_Multi`: qualified, in sheet order.
  - `Script_DeleteLastSheet_Err`.
  - `Cli_Eval_Qualified`: exit 0, plus an unknown sheet → exit 5.
  - `Cli_ImportCsv_Sheet_NewAndExisting`.
  - `Cli_ExportCsv_Sheet`.
- [ ] **Step 2:** Run `dotnet test tests/Gridline.Cli.Tests -c Release --filter "FullyQualifiedName~ScriptSheetTests|FullyQualifiedName~CliSheetTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the full non-UI suite, including `ProcessTests`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(cli): sheet commands and qualified addresses (CR1.7, §10)`.

---

### Task 10: GUI sheet tabs (CR1.8)

**Files:**
- Create: `src/Gridline.App/SheetTabs.cs` (UI-free model or helper where possible)
- Modify: `MainWindow.xaml(.cs)`, `SheetController.cs`, `DocumentSession.cs`, `GridControl.cs`
- Test: `tests/Gridline.App.Tests/SheetTabsTests.cs` (non-UI), `tests/Gridline.App.Tests/UiTests/SheetTabsUiTests.cs` (`Category=UI`)

**Interfaces:**
- Consumes: `Workbook.SheetNames`, `ActiveSheet`, `SheetsChanged`, `AddSheet`/`RenameSheet`/`DeleteSheet`/`MoveSheet`, `QualifiedAddress`.
- Produces:
  - **Tab strip.** A horizontal strip below the grid, above the status bar (AutomationId `SheetTabs`). Each tab is a `TabItem`/`ToggleButton` with AutomationId `SheetTab_<index>` and the sheet name as its header.
  - **Tab behaviour.**
    - Clicking a tab sets `ActiveSheet`.
    - Dragging a tab onto another tab calls `MoveSheet` to that index.
    - Context menu items, with AutomationIds:
      - `SheetAdd`: appends the next free `SheetN`.
      - `SheetRename`: opens a small input dialog, AutomationId `SheetRenameBox`, Enter commits.
      - `SheetDelete`: disabled when 1 sheet.
      - `SheetMoveLeft` / `SheetMoveRight`: disabled at the ends.
    - `ArgumentException`/`InvalidOperationException` → message box, nothing changes.
  - **Re-rendering.** The grid, the tabs, the name box and the formula bar re-render on `SheetsChanged`. The grid reads unqualified addresses, so it shows the active sheet.
  - **`SheetController.TryNavigate(text)`** accepts `QualifiedAddress`. A qualified address activates that sheet, then selects. An unknown sheet or bad syntax returns false, and the box reverts.
  - **Unchanged.** `SelectionValue`/`SelectionSum` behaviour, Undo/Redo menu enablement (refresh on `SheetsChanged` too), and the dirty title.
  - **New sheet name.** `SheetTabs.NextSheetName(IReadOnlyList<string> existing)` returns the smallest `SheetN` (N ≥ 1) not present case-insensitively.

- [ ] **Step 1: Write failing non-UI tests:**
  - `NextSheetName_SkipsExisting`: `["Sheet1","sheet3"]` → `"Sheet2"`.
  - `TryNavigate_Qualified_ActivatesSheet`.
  - `TryNavigate_UnknownSheet_False`.
  - `DocumentSession_DirtyAfterAddSheet_NotAfterActivate`.
- [ ] **Step 2:** Run `dotnet test tests/Gridline.App.Tests -c Release --filter "Category!=UI&FullyQualifiedName~SheetTabs"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Write the UI tests:
  - `Tabs_ShowSheet1`: `SheetTab_0` exists.
  - `ContextAdd_ThenClick_SwitchesGrid`: type in `Sheet2!A1` via the name box, then check that `SelectionValue` shows that cell's value only on `Sheet2`.
  - `NameBox_Qualified_SwitchesTab`.
  - `Delete_DisabledForLastSheet`.

  Run `dotnet test tests/Gridline.App.Tests -c Release --filter Category=UI`. Expected: PASS, including P-6.
- [ ] **Step 5:** Commit `feat(app): sheet tab strip and qualified name box (CR1.8, §11)`.

---

### Task 11: CR-1 exit (CR1.10, §13)

- [ ] **Step 1:** Run `dotnet build Gridline.sln -c Release`. Expected: 0 warnings, 0 errors.
- [ ] **Step 2:** Run `dotnet test Gridline.sln -c Release --filter "Category!=UI"`. Expected: all pass, including the Architecture tests. Core must still use no IO/Console/WPF types, and no NuGet packages may be added to `src/`.
- [ ] **Step 3:** Run `dotnet test Gridline.sln -c Release --filter "Category=UI|Category=Perf"`. Expected: PASS, P-1–P-8.
- [ ] **Step 4:** Add a `## CR-1 (multiple sheets)` section to DECISIONS.md that collects every CR-1 decision recorded in Tasks 1–10, each with its CR section. Commit `.claude/change request.md` with it. Commit message: `chore: CR-1 multiple sheets complete (CR1.10)`.
