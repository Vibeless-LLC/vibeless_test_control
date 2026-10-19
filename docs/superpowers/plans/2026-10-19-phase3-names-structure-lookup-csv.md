# Gridline Phase 3 — Names, Structure Edits, Lookup/Date, CSV — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Phase 3 scope from spec §13: lookup functions (§7.4), conditional aggregation (§7.5), date functions (§7.6), CSV (§8.5), row/column insert and delete (§9.5), names (§9.6), the Phase 3 CLI commands and the Phase 3 GUI items.

**Architecture:**
- **Names.** Names live in the Workbook as a case-insensitive map from upper-case name to `(DefinedCasing, RangeAddress? Target)`. A null target means `#REF!`. The evaluator reaches names through `IEvalContext.TryResolveName`, and the dependency graph reaches them through `SetNameTarget`.
- **Structure edits.** Each one is computed as a whole-sheet `CellEdit` diff plus a names before/after snapshot. The result goes through the Phase 2 `Commit` path, so undo, dirty tracking and recalc come for free.
- **Index arithmetic.** Insert and delete share one helper, `Addressing.StructuralShift`. Both the AST rewriter and name adjustment use it.

**Tech Stack:** as in Phases 1–2, plus `System.Text.Encoding.CodePages` from the BCL for Windows-1252.

**Spec:** `.claude/architecture.md`. Prerequisite: Phase 2 is complete (tag `phase-2`).

## Global Constraints

- All Phase 1 and 2 Global Constraints still apply. Earlier behaviour MUST NOT regress: run the full non-UI suite in every task.
- Name syntax is `^[A-Za-z_][A-Za-z0-9_.]{0,254}$`. A name must not be `TRUE` or `FALSE`, and must not match `^[A-Za-z]{1,3}[0-9]+$`. Names are unique case-insensitively.
- `refersTo` must be a fully absolute cell or range. It is stored upper-case, e.g. `$A$1:$C$10`.
- Valid date serials are `0`–`2958465`. Serial 60 is the fictitious 1900-02-29.
- CSV limits: at most 100,000 records and at most 702 fields per record. Export writes a UTF-8 BOM and CRLF after every record. The delimiter may not be `"`, CR or LF.

## Review Focus

1. **A defined name whose target is deleted, then saved and reloaded.** `refersTo` becomes `#REF!`, and the file must still load. Test: `Names_RefTargetSurvivesRoundTrip` (Task 2).
2. **An insert that would push data past row 100000, versus one that only pushes references past it.** Data past the limit throws `InvalidOperationException` and changes nothing. A reference past it becomes `#REF!` for a single cell, and a range clamps its bottom corner. Test: `InsertRows_DataOverflowThrows`, `InsertRows_RefOverflow` (Task 3).
3. **Undoing a structure edit restores verbatim formula text and names.** Text such as `= a1 + 1` must come back exactly, including formulas that were re-emitted. Test: `DeleteRows_UndoRestoresVerbatimAndNames` (Task 3).
4. **A CSV file without a BOM that contains Windows-1252 bytes** such as `0xE9`, which is `é`, must import as `é`. Test: `Import_Cp1252Fallback` (Task 7).
5. **Criteria against a range with errors and blanks**, as in `COUNTIF(A1:A5,"<>x")`. Blanks count, errors never match, and a numeric criterion ignores numeric text. Test: `Criteria_NotEqualText_IncludesBlanksExcludesErrors` (Task 5).

---

## File Structure

```
src/Gridline.Core/Addressing/StructuralShift.cs       index/span math for §9.5
src/Gridline.Core/Addressing/NameRules.cs             §9.6 name/refersTo validation
src/Gridline.Core/Parsing/ReferenceRewriter.cs        (modify) + Structural(...)
src/Gridline.Core/Workbook.cs                         (modify) names, insert/delete
src/Gridline.Core/Functions/LookupFunctions.cs        §7.4
src/Gridline.Core/Functions/Criteria.cs               §7.5 criteria parse/match
src/Gridline.Core/Functions/ConditionalFunctions.cs   §7.5
src/Gridline.Core/Functions/DateSerial.cs             serial <-> (y,m,d)
src/Gridline.Core/Functions/DateFunctions.cs          §7.6
src/Gridline.IO/GridlineFile.cs                       (modify) names
src/Gridline.IO/CsvFile.cs                            §8.5 import/export
src/Gridline.Cli/{CliApp,ScriptRunner}.cs             (modify) Phase 3 commands
src/Gridline.App/{MainWindow,GridControl}.*           (modify) CSV menus, header context menu
```

---

### Task 1: Names in Core (§9.6, §6.1, §6.7)

**Files:**
- Create: `src/Gridline.Core/Addressing/NameRules.cs`
- Modify: `src/Gridline.Core/Workbook.cs`, the Workbook's `IEvalContext` implementation, and `FromSnapshot` so it accepts names.
- Test: `tests/Gridline.Core.Tests/NameTests.cs`

**Interfaces:**
- Consumes:
  - Phase 1 `IEvalContext.TryResolveName` and `DependencyGraph.SetNameTarget`.
  - Phase 2 `Commit` with `NamesBefore`/`NamesAfter`, and `FormulaPrinter`'s `nameCasing`.
- Produces:
  - `public static class NameRules` in `Gridline.Core.Addressing`:
    - `static bool IsValidName(string name)`.
    - `static bool TryParseRefersTo(string s, out RangeAddress target)` requires `$` on every component and gives a 1×1 range for a single cell.
    - `static string FormatRefersTo(RangeAddress r)` gives `$B$1` or `$A$1:$C$10`.
  - Workbook gains these members:
    - `public void DefineName(string name, string refersTo)` and `public void DeleteName(string name)`, per §9.1.
    - `public IReadOnlyList<KeyValuePair<string,string>> GetNames()`. It returns names in their defined casing, sorted with `StringComparer.OrdinalIgnoreCase`. The value is `refersTo`, or `"#REF!"` when the target is deleted.
  - `FromSnapshot` accepts `refersTo == "#REF!"`. This exists so files round-trip; record it in DECISIONS.md. Other invalid values throw `ArgumentException`.

Evaluation:
- A cell name behaves like a `RefNode` argument, and a range name behaves like a range.
- An undefined name → `#NAME?`. A `#REF!` name → `#REF!`.

Dependencies:
- Defining, redefining or deleting a name re-plans with that name in `changedNames`.
- Each definition is one undo step, even when only the casing changes.
- Redefining a name with the identical casing and target creates no step.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory] [InlineData("Rate")] [InlineData("_x.y")] [InlineData("ABCD1")] [InlineData("A1B")]
public void ValidNames(string n) => Assert.True(NameRules.IsValidName(n));
[Theory] [InlineData("1a")] [InlineData("TRUE")] [InlineData("false")] [InlineData("A1")] [InlineData("ZZZ99")] [InlineData("a b")] [InlineData("")]
public void InvalidNames(string n) => Assert.False(NameRules.IsValidName(n));
[Fact] public void RefersTo_MustBeAbsolute() { var wb = new Workbook(); Assert.Throws<ArgumentException>(() => wb.DefineName("R", "B1")); Assert.Throws<ArgumentException>(() => wb.DefineName("R", "$B1")); Assert.Throws<ArgumentException>(() => wb.DefineName("R", "#REF!")); wb.DefineName("R", "$b$1"); Assert.Equal("$B$1", wb.GetNames()[0].Value); }
[Fact] public void UndefinedName_ResolvesWhenDefined() { var wb = new Workbook(); wb.SetCell("A1","=Rate*2"); Assert.Equal("E:#NAME?", wb.GetValue("A1").ToCanonicalString()); wb.SetCell("B1","4"); wb.DefineName("rate","$B$1"); Assert.Equal("N:8", wb.GetValue("A1").ToCanonicalString()); wb.SetCell("B1","5"); Assert.Equal("N:10", wb.GetValue("A1").ToCanonicalString()); }
[Fact] public void RangeName_InSum() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A2","2"); wb.DefineName("Total","$A$1:$A$2"); wb.SetCell("B1","=SUM(Total)"); Assert.Equal("N:3", wb.GetValue("B1").ToCanonicalString()); wb.SetCell("B2","=Total"); Assert.Equal("E:#VALUE!", wb.GetValue("B2").ToCanonicalString()); }
[Fact] public void Redefine_KeepsNewCasing_Recalcs() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A2","2"); wb.DefineName("x","$A$1"); wb.SetCell("B1","=X"); wb.DefineName("X","$A$2"); Assert.Equal("X", wb.GetNames()[0].Key); Assert.Equal("N:2", wb.GetValue("B1").ToCanonicalString()); }
[Fact] public void DeleteName_Unknown_Throws_Known_GivesNameError() { var wb = new Workbook(); Assert.Throws<ArgumentException>(() => wb.DeleteName("nope")); wb.DefineName("R","$A$1"); wb.SetCell("B1","=R"); wb.DeleteName("r"); Assert.Equal("E:#NAME?", wb.GetValue("B1").ToCanonicalString()); }
[Fact] public void Name_Cycle() { var wb = new Workbook(); wb.DefineName("Me","$A$1"); wb.SetCell("A1","=Me+1"); Assert.Equal("E:#CIRC!", wb.GetValue("A1").ToCanonicalString()); }
[Fact] public void Names_SortedOrdinalIgnoreCase() { var wb = new Workbook(); wb.DefineName("b","$A$1"); wb.DefineName("A","$A$1"); wb.DefineName("_z","$A$1"); Assert.Equal(new[]{"_z","A","b"}, wb.GetNames().Select(k => k.Key)); }
[Fact] public void Names_Undo() { var wb = new Workbook(); wb.DefineName("R","$A$1"); wb.DefineName("R","$A$2"); wb.Undo(); Assert.Equal("$A$1", wb.GetNames()[0].Value); wb.Undo(); Assert.Empty(wb.GetNames()); }
```

`ABCD1` is valid because four letters cannot be a column. Under `OrdinalIgnoreCase`, `_` (0x5F) sorts after letters that are upper-cased to 0x41–0x5A, so check the expected order in `Names_SortedOrdinalIgnoreCase` against `StringComparer.OrdinalIgnoreCase` itself. The test MUST sort with the comparer rather than hard-code the order if they disagree.

- [ ] **Step 2:** Run with `--filter NameTests`. Expected: FAIL.
- [ ] **Step 3:** Implement. Update the Phase 2 undo FsCheck generator with a `DefineName`/`DeleteName` op.
- [ ] **Step 4:** Run the non-UI suite. Expected: PASS. Commit: `feat(core): defined names (§9.6)`.

---

### Task 2: Names in the file format and CLI (§8.1, §8.2, §10.3)

**Files:**
- Modify: `src/Gridline.IO/GridlineFile.cs`, `src/Gridline.Cli/ScriptRunner.cs`
- Test: extend `GridlineFileTests` and add `tests/Gridline.Cli.Tests/ScriptNamesTests.cs`.

File format:
- Names are written as `"names": {\n    "Rate": "$B$1",\n    "Total": "$A$1:$A$10"\n  },` in `GetNames()` order, with each name and value escaped via `JsonEscaper`.
- On load, `names` must be an object whose values are strings. Anything else → `GridlineFormatException`.
- Duplicate names in the file, compared case-insensitively → `GridlineFormatException`.

CLI:
- `NAME n r` prints nothing.
- `UNNAME n` prints nothing.
- `NAMES` prints `NAME\t<name>\t<refersTo>` per name, sorted.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Save_Names_ExactBytes() { var wb = new Workbook(); wb.DefineName("Total","$A$1:$A$10"); wb.DefineName("Rate","$B$1"); Assert.Equal("{\n  \"format\": \"gridline\",\n  \"version\": 1,\n  \"names\": {\n    \"Rate\": \"$B$1\",\n    \"Total\": \"$A$1:$A$10\"\n  },\n  \"cells\": []\n}\n", GridlineFile.Serialize(wb)); }
[Fact] public void Load_InvalidName_Throws() => Assert.Throws<GridlineFormatException>(() => GridlineFile.Load("{\"format\":\"gridline\",\"version\":1,\"names\":{\"A1\":\"$A$1\"},\"cells\":[]}"u8));
[Fact] public void Load_RelativeRefersTo_Throws() => Assert.Throws<GridlineFormatException>(() => GridlineFile.Load("{\"format\":\"gridline\",\"version\":1,\"names\":{\"R\":\"A1\"},\"cells\":[]}"u8));
[Fact] public void Names_RefTargetSurvivesRoundTrip() { var wb = new Workbook(); wb.DefineName("R","$A$2"); wb.DeleteRows(2,1); var bytes = GridlineFile.SerializeToUtf8(wb); var back = GridlineFile.Load(bytes); Assert.Equal("#REF!", back.GetNames()[0].Value); Assert.Equal(bytes, GridlineFile.SerializeToUtf8(back)); }
// Cli
[Fact] public void NameCommands() => Assert.Equal("NAME\tRate\t$B$1\nA1\tN:6\n", Run("SET B1 3\nNAME Rate $b$1\nSET A1 =rate*2\nNAMES\nGET A1\nUNNAME rate\nNAMES\n","script").stdout);
[Fact] public void Name_Invalid_Err() => Assert.Equal(4, Run("NAME A1 $A$1\n","script").code);
```

`Names_RefTargetSurvivesRoundTrip` depends on Task 3's `DeleteRows`. Mark it `[Fact(Skip="needs Task 3")]` here, and remove the Skip in Task 3.

- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement names in the file format and the CLI.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(io,cli): names in file format and script (§8.1, §10.3)`.

---

### Task 3: Insert and delete rows and columns (§9.5)

**Files:**
- Create: `src/Gridline.Core/Addressing/StructuralShift.cs`
- Modify: `src/Gridline.Core/Parsing/ReferenceRewriter.cs`, `src/Gridline.Core/Workbook.cs`, `src/Gridline.Cli/ScriptRunner.cs`
- Test: `tests/Gridline.Core.Tests/Addressing/StructuralShiftTests.cs`, `tests/Gridline.Core.Tests/StructureEditTests.cs`, `tests/Gridline.Cli.Tests/ScriptStructureTests.cs`

**Interfaces:**
- Produces:
  - `public enum Axis { Row, Column }` and `public readonly record struct StructuralEdit(Axis Axis, int At, int Count, bool IsInsert)` in `Gridline.Core.Addressing`.
  - `public static class StructuralShift` with these members:
    - `static bool TryShiftIndex(int x, StructuralEdit e, int max, out int result)`:
      - Insert: `x ≥ At` → `x + Count`. The call fails when the result would exceed `max`.
      - Delete: `x` inside `[At, At+Count-1]` → fail. `x` below the span → `x − Count`.
    - `static bool TryShiftSpan(int lo, int hi, StructuralEdit e, int max, out int newLo, out int newHi)`:
      - Insert: shift each endpoint independently. If `newLo > max`, fail. If only `newHi > max`, clamp it to `max` (record in DECISIONS.md).
      - Delete: keep the surviving indices. If none survive, fail. Otherwise the new endpoints are the first and last survivors after the shift.
  - `internal static Node ReferenceRewriter.Structural(Node root, StructuralEdit e, out bool changed)`:
    - A changed `RefNode` keeps its `$` flags. A failed single ref becomes `ErrorNode("#REF!")`.
    - A `RangeNode` is normalized before shifting. A changed range is re-emitted top-left:bottom-right, and each corner keeps the `$` flags of the corner that supplied that row or column component. A failed range becomes `ErrorNode("#REF!")`.
    - Only the edit's axis is shifted.
  - Workbook gains `InsertRows`, `DeleteRows`, `InsertColumns` and `DeleteColumns`, per §9.1:
    - Argument validation per §9.5 throws `ArgumentOutOfRangeException`. For columns, use `MaxColumns`.
    - Insert first checks whether any cell with input would move past the max; if so it throws `InvalidOperationException` and changes nothing.
    - Then it computes the new full cell map. A moved cell keeps its input unless it is a non-parse-error formula with a changed ref, in which case the input is re-emitted via `FormulaPrinter` using the current name casing.
    - It diffs the old map against the new one into `CellEdit`s.
    - It adjusts the names with `TryShiftSpan` on both axes, giving `#REF!` (a null target) on failure.
    - It runs `Commit` once, recording one step.
  - CLI gains these commands, which print nothing:
    - `INSERTROWS r n` and `DELETEROWS r n`.
    - `INSERTCOLS C n` and `DELETECOLS C n`, where the column is given as letters and parsed with `ColumnName.TryParse`.
    - Bad arguments print `ERR`.

- [ ] **Step 1: Write failing tests**

```csharp
// StructuralShiftTests (rows, max 100000)
[Theory]
[InlineData(true, 3, 2, 1, 5, 1, 7)] [InlineData(true, 1, 2, 1, 5, 3, 7)] [InlineData(false, 3, 2, 1, 10, 1, 8)] [InlineData(false, 1, 2, 1, 10, 1, 8)] [InlineData(false, 5, 6, 3, 7, 3, 4)]
public void Span(bool ins, int at, int n, int lo, int hi, int elo, int ehi) { Assert.True(StructuralShift.TryShiftSpan(lo, hi, new(Axis.Row, at, n, ins), 100000, out var a, out var b)); Assert.Equal((elo, ehi), (a, b)); }
[Fact] public void Span_FullyDeleted_Fails() => Assert.False(StructuralShift.TryShiftSpan(3, 4, new(Axis.Row, 3, 2, false), 100000, out _, out _));
[Fact] public void Index_DeletedFails_InsertOverflowFails() { Assert.False(StructuralShift.TryShiftIndex(4, new(Axis.Row, 3, 2, false), 100000, out _)); Assert.False(StructuralShift.TryShiftIndex(99999, new(Axis.Row, 1, 5, true), 100000, out _)); }

// StructureEditTests
[Fact] public void InsertRows_ShiftsCellsAndRefs() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A3","=SUM(A1:A2)+A1"); wb.InsertRows(2, 1); Assert.Equal("=SUM(A1:A3)+A1", wb.GetInput("A4")); Assert.Equal("", wb.GetInput("A3")); }
[Fact] public void UnchangedFormula_KeepsVerbatimText() { var wb = new Workbook(); wb.SetCell("A5","= b1 + 1"); wb.InsertRows(3, 2); Assert.Equal("= b1 + 1", wb.GetInput("A7")); }
[Fact] public void DeleteRows_RefToDeletedIsRef() { var wb = new Workbook(); wb.SetCell("A2","5"); wb.SetCell("B5","=A2*2"); wb.DeleteRows(2, 1); Assert.Equal("=#REF!*2", wb.GetInput("B4")); Assert.Equal("E:#REF!", wb.GetValue("B4").ToCanonicalString()); }
[Fact] public void DeleteColumns_RangeShrinks() { var wb = new Workbook(); wb.SetCell("E1","=SUM($A$1:D1)"); wb.DeleteColumns(2, 2); Assert.Equal("=SUM($A$1:B1)", wb.GetInput("C1")); }
[Fact] public void InsertColumns_LettersAndNames() { var wb = new Workbook(); wb.DefineName("R","$B$1:$C$2"); wb.InsertColumns(2, 1); Assert.Equal("$C$1:$D$2", wb.GetNames()[0].Value); }
[Fact] public void Name_FullyDeleted_IsRef() { var wb = new Workbook(); wb.DefineName("R","$A$2:$A$3"); wb.SetCell("B1","=R"); wb.DeleteRows(2, 2); Assert.Equal("#REF!", wb.GetNames()[0].Value); Assert.Equal("E:#REF!", wb.GetValue("B1").ToCanonicalString()); }
[Fact] public void InsertRows_DataOverflowThrows() { var wb = new Workbook(); wb.SetCell("A100000","x"); Assert.Throws<InvalidOperationException>(() => wb.InsertRows(1, 1)); Assert.Equal("x", wb.GetInput("A100000")); Assert.True(wb.Undo()); Assert.False(wb.CanUndo); }
[Fact] public void InsertRows_RefOverflow() { var wb = new Workbook(); wb.SetCell("A1","=A99999+SUM(B99990:B100000)"); wb.InsertRows(2, 5); Assert.Equal("=#REF!+SUM(B99995:B100000)", wb.GetInput("A1")); }
[Fact] public void ArgValidation() { var wb = new Workbook(); Assert.Throws<ArgumentOutOfRangeException>(() => wb.InsertRows(0, 1)); Assert.Throws<ArgumentOutOfRangeException>(() => wb.InsertRows(1, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => wb.DeleteRows(99999, 3)); Assert.Throws<ArgumentOutOfRangeException>(() => wb.DeleteColumns(703, 1)); }
[Fact] public void DeleteRows_UndoRestoresVerbatimAndNames() { var wb = new Workbook(); wb.SetCell("A3","= a1 + 1"); wb.DefineName("R","$A$1"); wb.DeleteRows(1, 1); Assert.Equal("=#REF!+1", wb.GetInput("A2")); wb.Undo(); Assert.Equal("= a1 + 1", wb.GetInput("A3")); Assert.Equal("$A$1", wb.GetNames()[0].Value); }
[Fact] public void Row_ReflectsMove() { var wb = new Workbook(); wb.SetCell("A2","=ROW()"); wb.InsertRows(1, 3); Assert.Equal("N:5", wb.GetValue("A5").ToCanonicalString()); }
// Cli
[Fact] public void ScriptStructure() => Assert.Equal("C1\t=SUM(A1:B1)\n", Run("SET B1 =SUM(A1:A1)\nINSERTCOLS A 1\nDELETECOLS a 1\nSET C1 =SUM(A1:B1)\nINSERTROWS 5 2\nDELETEROWS 5 2\nINPUT C1\n","script").stdout);
```

- `Row_ReflectsMove` lands in Task 4, which adds ROW. Mark it `Skip` until then.
- Remove the `Skip` from Task 2's `Names_RefTargetSurvivesRoundTrip`.

- [ ] **Step 2:** Run with `--filter "StructuralShiftTests|StructureEditTests|ScriptStructureTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement. Add insert and delete ops to the undo FsCheck generator.
- [ ] **Step 4:** Run the non-UI suite. Expected: PASS. Commit: `feat(core,cli): insert/delete rows and columns (§9.5)`.

---

### Task 4: Lookup and reference functions (§7.4)

**Files:**
- Create: `src/Gridline.Core/Functions/LookupFunctions.cs`
- Test: `tests/Gridline.Core.Tests/Functions/LookupFunctionTests.cs`

**Interfaces:**
- Consumes: `Arg.Range` (a `RangeRef`), `Comparison.Compare`, `Wildcard.IsMatch`, `IEvalContext.FormulaRow` and `FormulaCol`.
- Arities:
  - VLOOKUP, HLOOKUP: 3–4.
  - INDEX: 2–3.
  - MATCH: 2–3.
  - CHOOSE: 2–255.
  - ROW, COLUMN: 0–1.
  - ROWS, COLUMNS: 1.
- A non-reference `table` or `range` argument gives `#VALUE!`. ROWS and COLUMNS of a scalar give 1 (record in DECISIONS.md).
- Exact equality uses three rules. Number vs number is exact. Bool vs bool is equal by value. Text vs text with wildcards uses `Wildcard.IsMatch(v, cell)`, case-insensitive. Different kinds never match. A lookup value that is an error propagates.
- An approximate match is a binary search for the last index whose key compares ≤ v under `Comparison.Compare`. Blank keys compare as per §6.3 adaptation.

- [ ] **Step 1: Write failing tests.** Use the fixture A1:B4 = `1,a / 3,b / 5,c / 7,d`.

```csharp
[Theory]
[InlineData("VLOOKUP(4,A1:B4,2)", "T:b")] [InlineData("VLOOKUP(0,A1:B4,2)", "E:#N/A")] [InlineData("VLOOKUP(9,A1:B4,2,TRUE)", "T:d")] [InlineData("VLOOKUP(4,A1:B4,2,FALSE)", "E:#N/A")]
[InlineData("VLOOKUP(5,A1:B4,0)", "E:#VALUE!")] [InlineData("VLOOKUP(5,A1:B4,3)", "E:#REF!")] [InlineData("VLOOKUP(\"C\",B1:B4,1,FALSE)", "T:c")] [InlineData("VLOOKUP(\"?\",B1:B4,1,FALSE)", "T:a")]
[InlineData("HLOOKUP(3,D1:G2,2)", "T:y")]
[InlineData("INDEX(A1:B4,2,2)", "T:b")] [InlineData("INDEX(B1:B4,3)", "T:c")] [InlineData("INDEX(A1:B4,2)", "E:#VALUE!")] [InlineData("INDEX(A1:B4,0,1)", "E:#VALUE!")] [InlineData("INDEX(A1:B4,5,1)", "E:#REF!")] [InlineData("INDEX(D1:G1,3)", "N:5")]
[InlineData("MATCH(5,A1:A4,0)", "N:3")] [InlineData("MATCH(6,A1:A4)", "N:3")] [InlineData("MATCH(\"b*\",B1:B4,0)", "N:2")] [InlineData("MATCH(1,A1:B4,0)", "E:#N/A")] [InlineData("MATCH(0,A1:A4)", "E:#N/A")] [InlineData("MATCH(4,H1:H3,-1)", "N:2")]
[InlineData("CHOOSE(2,1/0,\"x\")", "T:x")] [InlineData("CHOOSE(3,1,2)", "E:#VALUE!")] [InlineData("CHOOSE(1.9,\"a\",\"b\")", "T:a")] [InlineData("CHOOSE(1,A1:A2)", "E:#VALUE!")]
[InlineData("ROW(C7)", "N:7")] [InlineData("COLUMN(C7:D9)", "N:3")] [InlineData("ROWS(A1:B4)", "N:4")] [InlineData("COLUMNS(A1:B4)", "N:2")]
public void Lookups(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f, Fixture));
[Fact] public void Row_Omitted_OwnCell() { var wb = new Workbook(); wb.SetCell("C9","=ROW()+COLUMN()"); Assert.Equal("N:12", wb.GetValue("C9").ToCanonicalString()); }
```

`Fixture` holds A1:B4 as above. D1:G1 holds `1,3,5,7` and D2:G2 holds `x,y,z,w`. H1:H3 holds `9,5,1`, which is descending.

`CoreTestHost.Eval` places the formula in a cell **outside** these ranges, Z100. Remove the `Skip` on `Row_ReflectsMove` from Task 3.

- [ ] **Step 2:** Run with `--filter LookupFunctionTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the functions.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): lookup and reference functions (§7.4)`.

---

### Task 5: Criteria and conditional aggregation (§7.5)

**Files:**
- Create: `src/Gridline.Core/Functions/{Criteria,ConditionalFunctions}.cs`
- Test: `tests/Gridline.Core.Tests/Functions/ConditionalFunctionTests.cs`

**Interfaces:**
- Produces: `internal sealed class Criteria { static Criteria FromValue(CellValue c); bool Matches(CellValue cell); }`.
  - Text input is parsed as an operator followed by an operand, per §7.5.
  - A Number or Boolean value means equality.
  - A Blank criteria value is treated as the empty criterion `""` (record in DECISIONS.md).
  - An Error criteria value is handled by the caller, which returns it.
- Matching rules beyond the §7.5 table, all recorded in DECISIONS.md:
  - A Boolean operand matches Boolean cells only, ordered FALSE < TRUE.
  - `"<>"` alone matches any cell whose value is not Blank kind, so `""` text matches.
  - Error cells never match anything, including `<>text`.
- `sum_range` and `avg_range` are anchored at their top-left and resized to `range`'s dimensions. Positions that fall off the grid count as Blank.
- Arities: SUMIF and AVERAGEIF are 2–3, COUNTIF is 2.

- [ ] **Step 1: Write failing tests.** Use the fixture A1:A6 = `1`, `'5`, `x`, blank, `=1/0`, `XY`, and B1:B6 = `10,20,30,40,50,60`.

```csharp
[Theory]
[InlineData("COUNTIF(A1:A6,\">0\")", "N:1")] [InlineData("COUNTIF(A1:A6,5)", "N:0")] [InlineData("COUNTIF(A1:A6,\"x*\")", "N:2")] [InlineData("COUNTIF(A1:A6,\"X\")", "N:1")]
[InlineData("COUNTIF(A1:A6,\"\")", "N:1")] [InlineData("COUNTIF(A1:A6,\"=\")", "N:1")] [InlineData("COUNTIF(A1:A6,\"<>\")", "N:4")] [InlineData("COUNTIF(A1:A6,\"<y\")", "N:3")]
[InlineData("SUMIF(A1:A6,\"x*\",B1)", "N:90")] [InlineData("SUMIF(A1:A6,1,B1:B6)", "N:10")] [InlineData("SUMIF(A1:A6,\"<>x\",B1:B6)", "N:130")] [InlineData("SUMIF(B1:B6,\">25\")", "N:180")]
[InlineData("AVERAGEIF(A1:A6,\"zzz\")", "E:#DIV/0!")] [InlineData("AVERAGEIF(B1:B6,\">=40\")", "N:50")] [InlineData("COUNTIF(A1:A6,1/0)", "E:#DIV/0!")]
public void Conditional(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f, Fixture));
[Fact] public void Criteria_NotEqualText_IncludesBlanksExcludesErrors() => Assert.Equal("N:4", CoreTestHost.Eval("=COUNTIF(A1:A6,\"<>x\")", Fixture));
```

The fixture's `<>x` set is A1, A2, A4 and A6, which is 4 cells; A5 is an error and never matches. `SUMIF(A1:A6,"<>x",B1:B6)` therefore propagates no error, because A5 is not matched. The expected value is `N:130` (10+20+40+60).

`COUNTIF(A1:A6,"<y")` counts the text cells less than `Y`, which are `5`, `x` and `XY`. That gives `N:3`.

- [ ] **Step 2:** Run with `--filter ConditionalFunctionTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the functions.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): SUMIF/COUNTIF/AVERAGEIF with criteria (§7.5)`.

---

### Task 6: Date functions (§7.6)

**Files:**
- Create: `src/Gridline.Core/Functions/{DateSerial,DateFunctions}.cs`
- Test: `tests/Gridline.Core.Tests/Functions/DateFunctionTests.cs`

**Interfaces:**
- Produces: `internal static class DateSerial`:
  - `const int Max = 2958465`.
  - `static bool TryFromYmd(long y, long m, long d, out int serial)`. It normalizes the month rollover with floor division, then computes `FirstOfMonth(y', m') + d - 1`, and fails outside `[0, Max]`.
  - `static (int Y, int M, int D) ToYmd(int serial)`. Serial 0 gives (1900,1,0) and serial 60 gives (1900,2,29).

Serial ↔ calendar mapping:
- `FirstOfMonth`: 1900-01 → 1 and 1900-02 → 32.
- Months from 1900-03 onward → `(new DateOnly(y,m,1).DayNumber - new DateOnly(1899,12,30).DayNumber)`.
- Months before 1900-01 give a negative serial, and `TryFromYmd` then fails.
- `ToYmd` for serials 1–59 uses `DateOnly(1899,12,31).AddDays(s)`, and for ≥ 61 uses `DateOnly(1899,12,30).AddDays(s)`.

Arities:
- DATE: 3.
- YEAR, MONTH, DAY: 1.
- WEEKDAY: 1–2.
- EDATE, EOMONTH, DAYS: 2.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData("DATE(1900,1,1)", "N:1")] [InlineData("DATE(1900,2,28)", "N:59")] [InlineData("DATE(1900,2,29)", "N:60")] [InlineData("DATE(1900,3,1)", "N:61")] [InlineData("DATE(1900,1,0)", "N:0")]
[InlineData("DATE(2024,14,1)", "N:45689")] [InlineData("DATE(2024,1,0)", "N:45291")] [InlineData("DATE(24,1,1)", "N:8767")] [InlineData("DATE(-1,1,1)", "E:#NUM!")] [InlineData("DATE(10000,1,1)", "E:#NUM!")] [InlineData("DATE(9999,12,31)", "N:2958465")] [InlineData("DATE(9999,12,32)", "E:#NUM!")] [InlineData("DATE(1900,1,-1)", "E:#NUM!")]
[InlineData("YEAR(60)&\"-\"&MONTH(60)&\"-\"&DAY(60)", "T:1900-2-29")] [InlineData("DAY(0)", "N:0")] [InlineData("MONTH(0)", "N:1")] [InlineData("YEAR(-1)", "E:#NUM!")] [InlineData("YEAR(2958466)", "E:#NUM!")] [InlineData("YEAR(45291.9)", "N:2023")]
[InlineData("WEEKDAY(1)", "N:1")] [InlineData("WEEKDAY(1,2)", "N:7")] [InlineData("WEEKDAY(1,3)", "N:6")] [InlineData("WEEKDAY(2,2)", "N:1")] [InlineData("WEEKDAY(1,4)", "E:#NUM!")] [InlineData("WEEKDAY(0)", "N:7")]
[InlineData("EDATE(DATE(2024,1,31),1)", "N:45351")] [InlineData("EOMONTH(DATE(2024,1,15),1)", "N:45351")] [InlineData("EOMONTH(DATE(2024,1,15),-1)", "N:45291")] [InlineData("EDATE(2958465,1)", "E:#NUM!")]
[InlineData("DAYS(10.9,3.1)", "N:7")] [InlineData("\"2024-01-01\"+0", "E:#VALUE!")]
public void Dates(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f));
```

Some reference values:
- 2025-02-01 is 45689.
- 2023-12-31 is 45291.
- 1924-01-01 is 8767.
- 2024-02-29 is 45351.

Before committing, verify each expected serial with an independent `DateOnly` computation in a scratch test, and correct the literal if it disagrees.

- [ ] **Step 2:** Run with `--filter DateFunctionTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the functions.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): date functions with 1900 serial system (§7.6)`.

---

### Task 7: CSV import and export (§8.5, §10.2)

**Files:**
- Create: `src/Gridline.IO/CsvFile.cs`
- Modify: `src/Gridline.Cli/CliApp.cs`
- Test: `tests/Gridline.IO.Tests/{CsvFileTests,CsvRoundTripProperties}.cs`, `tests/Gridline.Cli.Tests/CsvCliTests.cs`

**Interfaces:**
- Consumes: Phase 2 `DelimitedReader.Parse` and `DelimitedMapping.ToInput`. Extend `DelimitedReader` with a record and field limit check (>100,000 records or >702 fields → `GridlineFormatException`), or do the check in `CsvFile`.
- Produces: `public static class CsvFile` (namespace `Gridline.IO`):
  - `static string Decode(ReadOnlySpan<byte> bytes)` follows the detection order:
    1. A UTF-8 BOM → UTF-8.
    2. `FF FE` → UTF-16 LE.
    3. `FE FF` → UTF-16 BE.
    4. A strict `UTF8Encoding(false, true)` decode succeeds → UTF-8.
    5. Otherwise use `CodePagesEncodingProvider.Instance.GetEncoding(1252)`.
  - `static Workbook Import(ReadOnlySpan<byte> bytes, char delimiter = ',')` builds the workbook through `Workbook.FromSnapshot`, so it is clean with no undo history.
  - `static Workbook ImportFromPath(string path, char delimiter = ',')`.
  - `static byte[] Export(Workbook wb, char delimiter = ',')`.
  - `static void ExportToPath(Workbook wb, string path, char delimiter = ',')`. It writes atomically, like `SaveToPath`.
  - An invalid delimiter (`"`, `\r`, `\n`) → `ArgumentException`. That applies to both import and export.
- Export fields:
  - Blank → empty. Number → `R` invariant (`-0` cannot occur). Boolean → `TRUE`/`FALSE`. Text → as is. Error → its code.
  - A field is quoted iff it contains the delimiter, `"`, CR or LF, or starts or ends with U+0020.
  - CRLF follows every record, and the output starts with a BOM. An empty workbook is exactly `EF BB BF`.
- CLI:
  - `import-csv <csv> <out> [--delimiter c]` and `export-csv <file> <out> [--delimiter c]`.
  - A `--delimiter` value that is not exactly one character, or is an invalid character, exits 1.
  - Format errors exit 3. I/O errors exit 2.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Import_MapsFields() { var wb = CsvFile.Import("a,-5,5%,true,=1+1,'x,\r\n,\"q,\"\"z\"\"\"\n"u8); Assert.Equal("a", wb.GetInput("A1")); Assert.Equal("N:-5", wb.GetValue("B1").ToCanonicalString()); Assert.Equal("N:0.05", wb.GetValue("C1").ToCanonicalString()); Assert.Equal("B:TRUE", wb.GetValue("D1").ToCanonicalString()); Assert.Equal("'=1+1", wb.GetInput("E1")); Assert.Equal("''x", wb.GetInput("F1")); Assert.Equal("", wb.GetInput("G1")); Assert.Equal("q,\"z\"", wb.GetInput("B2")); Assert.False(wb.IsDirty); }
[Fact] public void Import_Utf16LeBom() { var bytes = new byte[]{0xFF,0xFE}.Concat(Encoding.Unicode.GetBytes("é,1")).ToArray(); Assert.Equal("é", CsvFile.Import(bytes).GetInput("A1")); }
[Fact] public void Import_Utf16BeBom() { }
[Fact] public void Import_Cp1252Fallback() => Assert.Equal("é", CsvFile.Import(new byte[]{0xE9}).GetInput("A1"));
[Fact] public void Import_Unterminated_Throws() => Assert.Throws<GridlineFormatException>(() => CsvFile.Import("\"abc"u8));
[Fact] public void Import_TooManyFields_Throws() => Assert.Throws<GridlineFormatException>(() => CsvFile.Import(Encoding.UTF8.GetBytes(new string(',', 702))));
[Fact] public void Import_CustomDelimiter() => Assert.Equal("b", CsvFile.Import("a;b"u8, ';').GetInput("B1"));
[Fact] public void Export_Exact() { var wb = new Workbook(); wb.SetCell("A1","=1/4"); wb.SetCell("B1"," x"); wb.SetCell("C2","=1/0"); wb.SetCell("A2","a,b"); wb.SetCell("B2","TRUE"); Assert.Equal("﻿0.25,\" x\",\r\n\"a,b\",TRUE,#DIV/0!\r\n", new UTF8Encoding(false).GetString(CsvFile.Export(wb))); }
[Fact] public void Export_Empty_OnlyBom() => Assert.Equal(new byte[]{0xEF,0xBB,0xBF}, CsvFile.Export(new Workbook()));
[Fact] public void Export_QuotesEmbeddedQuotesAndNewlines() { var wb = new Workbook(); wb.SetCell("A1","a\"b\nc"); Assert.EndsWith("\"a\"\"b\nc\"\r\n", Encoding.UTF8.GetString(CsvFile.Export(wb))); }
[Theory] [InlineData('"')] [InlineData('\r')] [InlineData('\n')]
public void BadDelimiter_Throws(char d) { Assert.Throws<ArgumentException>(() => CsvFile.Export(new Workbook(), d)); Assert.Throws<ArgumentException>(() => CsvFile.Import("a"u8, d)); }
```

- In `Export_Exact`, decode the bytes with `new UTF8Encoding(false).GetString`, which keeps the BOM as `﻿`. Assert the exact string.
- `Import_Utf16BeBom` uses `Encoding.BigEndianUnicode` and is written like the LE test.

In `CsvRoundTripProperties`, add `[Property] Export_Import_PreservesValues`. It generates a grid of up to 10×10 cells. Each cell is one of:
- Blank.
- A finite double, stored as input `d.ToString("R")`.
- A boolean.
- Text built as `"x" + s`, where `s` is drawn from `[a-z ,;"\r\n\t]*`.
- Text built as `"'=" + s`, which tests injection.

After export and import, every cell's `GetValue` equals the original. Run the property once with delimiter `,` and once with `;`.

`CsvCliTests`:
- `import-csv` followed by `eval --all` gives the expected lines.
- `export-csv` writes the BOM.
- `--delimiter ab` exits 1.
- A missing csv exits 2.
- An unterminated quote exits 3.

- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement the CSV file support and CLI commands.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(io,cli): CSV import/export (§8.5, §10.2)`.

---

### Task 8: GUI Phase 3 items (§11.5)

**Files:**
- Modify: `src/Gridline.App/{MainWindow.xaml,MainWindow.xaml.cs,GridControl.cs}`
- Test: extend `tests/Gridline.App.Tests/UiTests/MenuUiTests.cs` (Category=UI).

Behaviour:
- **File → Import CSV.** It runs the unsaved-changes prompt first, then `CsvFile.ImportFromPath`. The result becomes an untitled document marked dirty, using the `RecoveredFrom`-style flag from Phase 2 Task 10 or a new `ImportedFrom` flag (record in DECISIONS.md).
- **File → Export CSV.** It calls `CsvFile.ExportToPath`. The document's dirty state is unchanged.
- **Header clicks.** Clicking a row header selects `A{r}:ZZ{r}`, and Shift+click extends across rows. Column headers are symmetric.
- **Header context menu.** Right-clicking a header opens a `ContextMenu` with **Insert** and **Delete**:
  - The menu applies to the selected rows or columns when the click lands inside the current full-row or full-column selection. Otherwise it applies to the clicked header only.
  - It calls `InsertRows(firstRow, count)` / `DeleteRows(...)` or the column equivalents.
  - `InvalidOperationException` and `ArgumentOutOfRangeException` show a message box.

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** Add UI tests:
  - `RowHeader_InsertShiftsData`: put `5` in A1, right-click row header 1, choose Insert. A2's `SelectionValue` is `N:5`.
  - `ImportCsv_MenuExists`: the File menu contains the `Import CSV` and `Export CSV` items.
- [ ] **Step 3:** Run `dotnet test tests/Gridline.App.Tests -c Release --filter Category=UI`. Expected: PASS. Commit: `feat(app): CSV menus and header insert/delete (§11.5)`.

---

### Task 9: Phase 3 exit (§13)

- [ ] **Step 1:** Run `dotnet build Gridline.sln -c Release`. Expected: 0 errors and 0 warnings.
- [ ] **Step 2:** Run `dotnet test Gridline.sln -c Release --filter "Category!=UI"`. Expected: all tests pass. That covers Phases 1–3, with no regressions.
- [ ] **Step 3:** Run `dotnet test Gridline.sln -c Release --filter "Category=UI|Category=Perf"`. Expected: PASS.
- [ ] **Step 4:** Check `DECISIONS.md` for completeness, commit `chore: phase 3 complete (§13)`, and tag `phase-3`.
