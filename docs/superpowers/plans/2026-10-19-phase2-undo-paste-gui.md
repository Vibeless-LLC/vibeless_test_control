# Gridline Phase 2 — Undo, Paste, GUI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Phase 2 scope from spec §13: canonical formula printing (§5.5), undo and dirty tracking (§9.3), CopyPaste (§9.4), the Phase 2 CLI commands, the WPF GUI (§11, minus Phase 3 items) and P-6.

**Architecture:** Every Workbook mutation goes through a single internal `Commit(diff)` path. That path records an `EditStep` in `Gridline.Core.Commands.UndoHistory` and runs one recalc. Undo and redo replay a step's input diffs through the same path, but skip recording. Paste rewrites formulas through a Parsing-layer AST shifter and printer. The GUI is a single window, and its grid is a custom `FrameworkElement` that draws only the visible cells in `OnRender`.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), xUnit, FsCheck.Xunit, FlaUI.UIA3.

**Spec:** `.claude/architecture.md`. Prerequisite: the Phase 1 plan is complete (tag `phase-1`).

## Global Constraints

- All Phase 1 Global Constraints still apply. Phase 1 behaviour MUST NOT regress: run the full non-UI suite in every task.
- App: `net10.0-windows`, `<UseWPF>true</UseWPF>`, Nullable enabled, no NuGet packages. It references Core and IO only, never Cli.
- `Workbook.UndoLimit = 100`.
- Window title: `Gridline - <file name or Untitled>`, with ` *` appended when dirty. Default size 1280×800.
- AutomationIds are exactly: `NameBox`, `FormulaBar`, `Grid`, `SelectionValue`, `SelectionSum`, `RecoveryDialog`, `MenuFile`, `MenuEdit`, `MenuUndo`, `MenuRedo`, `MenuSave`, `MenuOpen`.
- Recovery directory: `%LOCALAPPDATA%\Gridline\Recovery\<session-guid>.gridline`. Autosave runs every 60 s while dirty.
- UI tests carry `[Trait("Category","UI")]`.

## Review Focus

1. **Undo across a failed call.** A `CopyPaste` that throws because it runs off the grid must create no undo step and leave `IsDirty` unchanged. Test: `Paste_OffGrid_NoStepNoChange` (Task 3).
2. **Dirty after save-then-undo-then-new-edit.** Save, undo, then make a new edit. The redo branch that held the saved position is gone, so the workbook must stay dirty even after more undos and redos. Test: `Dirty_SavedPositionDiscardedByNewBranch` (Task 2).
3. **Overlapping paste that shifts a formula onto its own source.** `A1:A3` pasted to `A2`, where `A2=A1*2`. The source must be snapshotted. Test: `Paste_Overlapping_UsesSnapshot` (Task 3).
4. **Pasting a formula with a parse error, or one whose references go off-grid.** A parse-error formula is copied verbatim. Off-grid references become `#REF!`, and a range with one off-grid corner becomes a whole `#REF!`. Test: `Paste_ParseErrorVerbatim`, `Paste_RangeCornerOffGrid_WholeRef` (Task 3).
5. **GUI editing commits on focus loss and navigation via the name box with `$` or lowercase.** Typing `$b$7` in the name box must navigate to `B7` and show `B7`. Test: `NameBox_AcceptsAbsoluteLowercase` (Task 7 UI test).

---

## File Structure

```
src/Gridline.Core/Parsing/FormulaPrinter.cs      §5.5 canonical printing
src/Gridline.Core/Parsing/ReferenceRewriter.cs   shift (paste); Phase 3 adds insert/delete
src/Gridline.Core/Commands/EditStep.cs           cell diffs + names before/after
src/Gridline.Core/Commands/UndoHistory.cs        stacks, limit, saved position
src/Gridline.Core/Workbook.cs                    (modify) Commit path, Phase 2 members
src/Gridline.IO/DelimitedReader.cs               RFC-4180 reader shared by clipboard (P2) and CSV (P3)
src/Gridline.Cli/ScriptRunner.cs                 (modify) Phase 2 commands
src/Gridline.App/
  Gridline.App.csproj App.xaml(.cs) MainWindow.xaml(.cs)
  DocumentSession.cs     file path, title, dirty, open/save/new logic (UI-free)
  GridControl.cs         virtualized grid FrameworkElement + AutomationPeer
  GridSelection.cs       active cell + anchor → range
  CellFormatter.cs       §11.3 display text and alignment
  ClipboardService.cs    internal copy buffer + system clipboard TSV
  RecoveryService.cs     autosave timer, recovery files, startup scan
  RecoveryDialog.xaml(.cs)
  UnsavedChangesDialog.cs  Save / Don't Save / Cancel
tests/Gridline.App.Tests/
  DocumentSessionTests.cs CellFormatterTests.cs (non-UI)
  UiTests/*.cs (Category=UI, FlaUI)
```

---

### Task 1: Canonical printer and paste shifter (§5.5, §9.4)

**Files:**
- Create: `src/Gridline.Core/Parsing/{FormulaPrinter,ReferenceRewriter}.cs`
- Test: `tests/Gridline.Core.Tests/Parsing/FormulaPrinterTests.cs`

**Interfaces:**
- Consumes: the Phase 1 AST (`ParenNode`, `NumberNode.Lexeme`, `RefNode` with abs flags, `NameNode`, `CallNode` with null args).
- Produces (all `internal`):
  - `static class FormulaPrinter { static string Print(Node root, Func<string,string?> nameCasing); }`. It returns the text **with** a leading `=`. `nameCasing(upper)` returns the defined casing, or null, in which case the printer uses the name as written.
  - `static class ReferenceRewriter { static Node Shift(Node root, int dRow, int dCol, out bool changed); }`:
    - Relative components shift; `$` components stay put.
    - A ref shifted outside the grid becomes `ErrorNode("#REF!")`.
    - In a `RangeNode`, if either corner becomes invalid, the whole range becomes `ErrorNode("#REF!")`.
    - `changed` is true if any ref moved or became `#REF!`.

Printing rules:
- No whitespace.
- Function names, booleans, error codes and refs are upper-case.
- Each ref keeps its own `$`.
- A number prints its `Lexeme`.
- A string prints as `"` + value with `"`→`""` + `"`.
- An omitted argument prints as an empty string between commas.
- A `ParenNode` prints as `(`…`)`.
- `UnaryNode`, `PercentNode` and `BinaryNode` print their operators verbatim.

- [ ] **Step 1: Write failing tests**

```csharp
string Canon(string f) => FormulaPrinter.Print(Parser.Parse(f[1..], Arity).Root!, _ => null);
[Fact] public void SpecExample() => Assert.Equal("=SUM(A1,$B$2:C3)*(2.50+x)", Canon("= sum( a1 , $b$2:C3 ) * (2.50+ x)"));
[Fact] public void StringsEscaped() => Assert.Equal("=\"a\"\"b\"&\"\"", Canon("=\"a\"\"b\" & \"\""));
[Fact] public void OmittedArgs() => Assert.Equal("=IF(A1,,3)", Canon("=if(a1, ,3)"));
[Fact] public void ParensPreservedNotAdded() => Assert.Equal("=((1))+-2^2%", Canon("=((1)) + -2^2%"));
[Fact] public void BoolsErrorsUnknownFuncsUpper() => Assert.Equal("=FOO(TRUE,#N/A)", Canon("=foo(true,#N/A)"));
[Fact] public void NameCasingFromDefinition() => Assert.Equal("=Rate*2", FormulaPrinter.Print(Parser.Parse("RATE*2", Arity).Root!, n => n == "RATE" ? "Rate" : null));
[Fact] public void Shift_RelativeAbsoluteMixed() { var n = ReferenceRewriter.Shift(Parser.Parse("A1+$A1+A$1+$A$1", Arity).Root!, 2, 1, out var ch); Assert.True(ch); Assert.Equal("=B3+$A3+B$1+$A$1", FormulaPrinter.Print(n, _ => null)); }
[Fact] public void Shift_OffGrid_Ref() => Assert.Equal("=#REF!+1", FormulaPrinter.Print(ReferenceRewriter.Shift(Parser.Parse("A1+1", Arity).Root!, -1, 0, out _), _ => null));
[Fact] public void Shift_RangeCornerOffGrid_WholeRef() => Assert.Equal("=SUM(#REF!)", FormulaPrinter.Print(ReferenceRewriter.Shift(Parser.Parse("SUM(A1:B2)", Arity).Root!, 0, 701, out _), _ => null));
```

- [ ] **Step 2:** Run with `--filter FormulaPrinterTests`. Expected: FAIL.
- [ ] **Step 3:** Implement both classes as recursive AST walks; recursion is allowed within a single formula.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(core): canonical formula printer and reference shifter (§5.5)`.

---

### Task 2: Undo history, ApplyBatch, dirty tracking (§9.3)

**Files:**
- Create: `src/Gridline.Core/Commands/{EditStep,UndoHistory}.cs`
- Modify: `src/Gridline.Core/Workbook.cs`
- Test: `tests/Gridline.Core.Tests/UndoTests.cs`, `tests/Gridline.Core.Tests/UndoProperties.cs`

**Interfaces:**
- Produces:
  - `internal sealed record CellEdit(int Key, string OldInput, string NewInput)`.
  - `internal sealed record EditStep(long Id, IReadOnlyList<CellEdit> Cells, IReadOnlyList<KeyValuePair<string,string>>? NamesBefore, IReadOnlyList<KeyValuePair<string,string>>? NamesAfter)`. Null names mean "names unchanged"; Phase 3 uses them.
  - `internal sealed class UndoHistory(int limit)`:
    - `void Push(EditStep s)` clears redo and drops the oldest step past the limit.
    - `bool TryUndo(out EditStep s)`, `bool TryRedo(out EditStep s)`.
    - `bool CanUndo`, `bool CanRedo`.
    - `long CurrentPosition` is the Id of the top undo step, or `BaseId` when the stack is empty.
    - `long BaseId` starts at 0. When the oldest step is dropped, `BaseId` becomes that step's Id.
    - `void MarkSaved()` sets `SavedPosition = CurrentPosition`.
    - `bool IsDirty => CurrentPosition != SavedPosition || savedPositionLost`.
    - `savedPositionLost` becomes true in two cases. The first is when a `Push` discards a redo entry whose Id equals `SavedPosition`. The second is when a drop makes `SavedPosition` neither `BaseId` nor any Id still on either stack. `MarkSaved` resets it.
  - `Workbook` gains these public members:
    - `ApplyBatch`, `Undo`, `Redo`, `CanUndo`, `CanRedo`, `IsDirty` and `MarkSaved`, per §9.1.
    - A `private void Commit(IReadOnlyList<CellEdit> edits, names…, bool record)`. It applies the inputs, recalcs once over all changed keys, raises `CellsChanged` once, and pushes a step only when `record` is true and something changed.
    - `SetCell` becomes a one-edit `Commit`.

Edge cases:
- Edits whose new input equals the old one are filtered out before deciding "something changed".
- In `ApplyBatch`, the last duplicate wins. Duplicates are compared by key, so they are case-insensitive.
- An empty batch, or a batch of no-ops, creates no step.
- `ApplyBatch` validates every address and input length before it applies anything.
- `EvaluatedFormulaCount` after a batch, undo or redo counts the formula cells in the union of the changed keys and all their transitive dependents.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Undo_RestoresVerbatimFormula() { var wb = new Workbook(); wb.SetCell("A1","= 1 +  2"); wb.SetCell("A1","5"); Assert.True(wb.Undo()); Assert.Equal("= 1 +  2", wb.GetInput("A1")); Assert.True(wb.Redo()); Assert.Equal("5", wb.GetInput("A1")); }
[Fact] public void Undo_Nothing_False() { var wb = new Workbook(); Assert.False(wb.Undo()); Assert.False(wb.Redo()); Assert.False(wb.CanUndo); }
[Fact] public void SameInput_NoStep() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A1","1"); wb.Undo(); Assert.False(wb.CanUndo); }
[Fact] public void NewMutation_ClearsRedo() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.Undo(); wb.SetCell("B1","2"); Assert.False(wb.CanRedo); }
[Fact] public void Limit100_DropsOldest() { var wb = new Workbook(); for (int i = 1; i <= 101; i++) wb.SetCell("A1", i.ToString()); int n = 0; while (wb.Undo()) n++; Assert.Equal(100, n); Assert.Equal("1", wb.GetInput("A1")); }
[Fact] public void Batch_OneStep_LastWins_Atomic() { var wb = new Workbook(); wb.ApplyBatch([new("A1","1"), new("B1","2"), new("a1","3")]); Assert.Equal("3", wb.GetInput("A1")); wb.Undo(); Assert.Empty(wb.GetUsedAddresses()); Assert.Throws<ArgumentException>(() => wb.ApplyBatch([new("A1","9"), new("A0","1")])); Assert.Empty(wb.GetUsedAddresses()); }
[Fact] public void Batch_Empty_NoStep() { var wb = new Workbook(); wb.ApplyBatch([]); Assert.False(wb.CanUndo); Assert.False(wb.IsDirty); }
[Fact] public void Dirty_UndoToSavedIsClean() { var wb = new Workbook(); Assert.False(wb.IsDirty); wb.SetCell("A1","1"); Assert.True(wb.IsDirty); wb.MarkSaved(); wb.SetCell("A1","2"); wb.Undo(); Assert.False(wb.IsDirty); wb.Undo(); Assert.True(wb.IsDirty); }
[Fact] public void Dirty_SavedPositionDiscardedByNewBranch() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.MarkSaved(); wb.Undo(); wb.SetCell("A1","2"); wb.Undo(); Assert.True(wb.IsDirty); }
[Fact] public void Dirty_SavedPositionDropped_StaysDirty() { var wb = new Workbook(); wb.MarkSaved(); for (int i = 0; i < 101; i++) wb.SetCell("A1", i.ToString()); while (wb.Undo()) { } Assert.True(wb.IsDirty); wb.MarkSaved(); Assert.False(wb.IsDirty); }
[Fact] public void Undo_RecalcsAndRaisesEvent() { var wb = new Workbook(); wb.SetCell("B1","=A1*2"); wb.SetCell("A1","3"); int n = 0; wb.CellsChanged += (_, _) => n++; wb.Undo(); Assert.Equal("N:0", wb.GetValue("B1").ToCanonicalString()); Assert.Equal(1, n); }
```

In `UndoProperties`, add `[Property(MaxTest = 200)] RandomOps_UndoAll_RestoresInitial`. It generates a sequence of up to 30 ops from these kinds:
- SetCell over a small address pool (A1:C3), with inputs that include formulas.
- ApplyBatch.
- CopyPaste, once Task 3 lands; add the generator case then.

After applying the ops, undo until it returns false. Every address's input must then equal the initial state, which is empty.

- [ ] **Step 2:** Run with `--filter "UndoTests|UndoProperties"`. Expected: FAIL.
- [ ] **Step 3:** Implement. Update `GridlineFile.Load` tests: a loaded workbook has `IsDirty == false` and `CanUndo == false`.
- [ ] **Step 4:** Run `dotnet test Gridline.sln -c Release --filter "Category!=UI"`. Expected: PASS. Commit: `feat(core): undo/redo, ApplyBatch, dirty tracking (§9.3)`.

---

### Task 3: CopyPaste (§9.4)

**Files:**
- Modify: `src/Gridline.Core/Workbook.cs`
- Test: `tests/Gridline.Core.Tests/CopyPasteTests.cs`, and extend `UndoProperties`.

**Interfaces:**
- Consumes: Task 1 `ReferenceRewriter.Shift` and `FormulaPrinter.Print`; Task 2 `Commit`.
- Produces: `public void CopyPaste(string sourceRange, string destinationTopLeft)`. It accepts a single cell or a range as the source.

Algorithm:
1. Validate both addresses and the destination extent.
2. Snapshot the source inputs into a `rows × cols` string array.
3. For each position, build a `CellEdit`:
   - A blank source cell gives `""`.
   - A non-formula input is copied verbatim.
   - A formula that is a parse error is copied verbatim (record in DECISIONS.md).
   - Any other formula becomes `FormulaPrinter.Print(Shift(root, dR, dC))`.
4. `Commit` the edits as one step.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Paste_ShiftsRelative() { var wb = new Workbook(); wb.SetCell("A1","=b1+$C$1"); wb.CopyPaste("A1","B3"); Assert.Equal("=C3+$C$1", wb.GetInput("B3")); }
[Fact] public void Paste_ZeroOffsetStillCanonical() { var wb = new Workbook(); wb.SetCell("A1","= sum( b1 )"); wb.CopyPaste("A1","A1"); Assert.Equal("=SUM(B1)", wb.GetInput("A1")); }
[Fact] public void Paste_BlankClearsDestination() { var wb = new Workbook(); wb.SetCell("B2","x"); wb.CopyPaste("A1:A2","B1"); Assert.Equal("", wb.GetInput("B2")); }
[Fact] public void Paste_Overlapping_UsesSnapshot() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A2","=A1*2"); wb.SetCell("A3","x"); wb.CopyPaste("A1:A3","A2"); Assert.Equal("1", wb.GetInput("A2")); Assert.Equal("=A2*2", wb.GetInput("A3")); Assert.Equal("x", wb.GetInput("A4")); }
[Fact] public void Paste_OffGrid_NoStepNoChange() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.MarkSaved(); Assert.Throws<ArgumentException>(() => wb.CopyPaste("A1:B1","ZZ1")); Assert.False(wb.IsDirty); Assert.True(wb.CanUndo); wb.Undo(); Assert.False(wb.CanUndo); }
[Fact] public void Paste_RefShiftsOffGrid() { var wb = new Workbook(); wb.SetCell("B2","=A1"); wb.CopyPaste("B2","A1"); Assert.Equal("=#REF!", wb.GetInput("A1")); Assert.Equal("E:#REF!", wb.GetValue("A1").ToCanonicalString()); }
[Fact] public void Paste_RangeCornerOffGrid_WholeRef() { var wb = new Workbook(); wb.SetCell("B1","=SUM(A1:A2)"); wb.CopyPaste("B1","A1"); Assert.Equal("=SUM(#REF!)", wb.GetInput("A1")); }
[Fact] public void Paste_ParseErrorVerbatim() { var wb = new Workbook(); wb.SetCell("A1","=1+"); wb.CopyPaste("A1","B1"); Assert.Equal("=1+", wb.GetInput("B1")); }
[Fact] public void Paste_OneUndoStep() { var wb = new Workbook(); wb.SetCell("A1","1"); wb.SetCell("A2","2"); wb.CopyPaste("A1:A2","B1"); wb.Undo(); Assert.Equal("", wb.GetInput("B1")); Assert.Equal("", wb.GetInput("B2")); }
```

- [ ] **Step 2:** Run with `--filter CopyPasteTests`. Expected: FAIL.
- [ ] **Step 3:** Implement CopyPaste, and add the CopyPaste generator case to `UndoProperties`.
- [ ] **Step 4:** Run the non-UI suite. Expected: PASS. Commit: `feat(core): CopyPaste with reference shifting (§9.4)`.

---

### Task 4: CLI Phase 2 commands (§10.3)

**Files:**
- Modify: `src/Gridline.Cli/ScriptRunner.cs`
- Test: `tests/Gridline.Cli.Tests/ScriptPhase2Tests.cs`. Delete `Script_Phase2CommandsNotImplemented`.

Behaviour:
- `UNDO` and `REDO` print `OK` or `NOOP`.
- `DIRTY` prints `TRUE` or `FALSE`.
- `PASTE src dst` prints nothing.
- `SAVE` now calls `MarkSaved()` after the save succeeds.
- `BEGIN` opens a batch. SET and SETX lines inside it are buffered.
- `END` applies the buffered edits with `ApplyBatch`. An `END` without a `BEGIN` is an ERR.
- Any other command inside a batch prints `ERR` and is not executed. The batch stays open.
- `BEGIN` while a batch is open is an ERR.
- At EOF, an unclosed batch is discarded and prints `ERR\tunterminated BEGIN` (record in DECISIONS.md).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void UndoRedo() => Assert.Equal("NOOP\nOK\nA1\tBLANK\nOK\nA1\tN:1\n", Run("UNDO\nSET A1 1\nUNDO\nGET A1\nREDO\nGET A1\n","script").stdout);
[Fact] public void BeginEnd_OneStep() => Assert.Equal("OK\nA1\tBLANK\nB1\tBLANK\n", Run("BEGIN\nSET A1 1\nSETX B1 a\\tb\nEND\nUNDO\nGET A1\nGET B1\n","script").stdout);
[Fact] public void Begin_OtherCommandErr() { var r = Run("BEGIN\nGET A1\nSET A1 1\nEND\nGET A1\n","script"); Assert.Equal(4, r.code); Assert.StartsWith("ERR\t", r.stdout); Assert.EndsWith("A1\tN:1\n", r.stdout); }
[Fact] public void Dirty_And_SaveMarks() { /* SET A1 1; DIRTY → TRUE; SAVE tmp; DIRTY → FALSE */ }
[Fact] public void Paste() => Assert.Equal("B2\t=B1+1\n", Run("SET A1 =A0+1\nSET A2 =A1+1\nPASTE A2 B2\nINPUT B2\n","script").stdout);
```

- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement the commands.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(cli): phase 2 script commands (§10.3)`.

---

### Task 5: App shell — window, layout, menus, document session (§11.1, §11.2, §11.5)

**Files:**
- Create: `src/Gridline.App/{Gridline.App.csproj,App.xaml,App.xaml.cs,MainWindow.xaml,MainWindow.xaml.cs,DocumentSession.cs}` and `tests/Gridline.App.Tests/{Gridline.App.Tests.csproj,DocumentSessionTests.cs}`. The test project targets `net10.0-windows` and adds the App to the solution.
- Modify: `tests/Gridline.Architecture.Tests` adds the rule "App does not depend on Gridline.Cli".

**Interfaces:**
- Produces: `public sealed class DocumentSession` (UI-free):
  - `Workbook Workbook { get; }`, `string? FilePath { get; }`.
  - `string Title` gives `"Gridline - " + (FilePath is null ? "Untitled" : Path.GetFileName(FilePath)) + (Workbook.IsDirty ? " *" : "")`.
  - `void New()`, `void Open(string path)`, `void Save()` (requires `FilePath`), and `void SaveAs(string path)`. Saving calls `GridlineFile.SaveToPath` and then `Workbook.MarkSaved()`.
  - `event EventHandler? StateChanged` fires on any dirty, title or workbook change.
  - `Guid SessionId`.

- MainWindow layout is a DockPanel with four parts:
  - a `Menu` docked top;
  - a Grid row holding `TextBox NameBox` (width 100) and `TextBox FormulaBar`;
  - a `StatusBar` docked bottom, holding `TextBlock SelectionValue` and `TextBlock SelectionSum`;
  - the grid host filling the rest. Use a placeholder `Border` with `AutomationProperties.AutomationId="Grid"` until Task 6.
- Menu items:
  - File (`MenuFile`): New Ctrl+N, Open Ctrl+O (`MenuOpen`), Save Ctrl+S (`MenuSave`), Save As Ctrl+Shift+S, Exit.
  - Edit (`MenuEdit`): Undo Ctrl+Z (`MenuUndo`), Redo Ctrl+Y (`MenuRedo`), Copy Ctrl+C, Paste Ctrl+V.
  - Use `RoutedUICommand`/`InputBindings` on the window.
- `Width="1280" Height="800"`. Bind the title to `DocumentSession.Title`.

- [ ] **Step 1: Write failing tests** (`DocumentSessionTests`, non-UI)

```csharp
[Fact] public void Title_UntitledClean() => Assert.Equal("Gridline - Untitled", new DocumentSession().Title);
[Fact] public void Title_DirtyStar() { var s = new DocumentSession(); s.Workbook.SetCell("A1","1"); Assert.Equal("Gridline - Untitled *", s.Title); }
[Fact] public void SaveAs_SetsNameAndClean() { var s = new DocumentSession(); s.Workbook.SetCell("A1","1"); var p = Path.Combine(TempDir, "book.gridline"); s.SaveAs(p); Assert.Equal("Gridline - book.gridline", s.Title); Assert.False(s.Workbook.IsDirty); }
[Fact] public void Open_LoadsClean() { /* save a file, Open it, Title has file name, not dirty, values loaded */ }
```

- [ ] **Step 2:** Run `dotnet test tests/Gridline.App.Tests -c Release --filter "Category!=UI"`. Expected: FAIL.
- [ ] **Step 3:** Implement the session and the window shell.
- [ ] **Step 4:** Run the tests. Expected: PASS. Also run `dotnet build Gridline.sln -c Release` and the architecture tests. Commit: `feat(app): window shell, menus, document session (§11.1, §11.2, §11.5)`.

---

### Task 6: Virtualized grid control (§11.3)

**Files:**
- Create: `src/Gridline.App/{GridControl,GridSelection,CellFormatter}.cs`
- Modify: `MainWindow.xaml` replaces the placeholder with `GridControl` (AutomationId `Grid`) inside a host that has a vertical and a horizontal `ScrollBar`.
- Test: `tests/Gridline.App.Tests/{CellFormatterTests,GridSelectionTests}.cs` (non-UI).

**Interfaces:**
- Produces:
  - `public static class CellFormatter { static (string Text, TextAlignment Align) Format(CellValue v); }`:
    - A number formats as `G10` invariant and right-aligns.
    - Text left-aligns.
    - A boolean shows `TRUE`/`FALSE` centred.
    - An error shows its code centred.
    - A blank shows `""`.
  - `public sealed class GridSelection`:
    - `CellAddress Active`, `CellAddress Anchor`, `RangeAddress Range`.
    - `void MoveTo(CellAddress a, bool extend)`.
    - `void MoveBy(int dRow, int dCol, bool extend)`, clamped to the grid.
    - `event EventHandler? Changed`.
  - `public sealed class GridControl : FrameworkElement`:
    - `Workbook? Workbook` (subscribes to `CellsChanged` and calls `InvalidateVisual`).
    - `GridSelection Selection`.
    - `int TopRow`, `int LeftCol`, plus `ScrollIntoView(CellAddress)`.
    - `OnRender` draws only the visible headers and cells, using `FormattedText` or cached `GlyphRun`s. It uses a fixed row height of 22 and a column width of 80. Headers show `A…ZZ` and `1…100000`.
    - Mouse down selects; drag with the button held extends the selection; Shift+click extends.
    - `OnCreateAutomationPeer` returns a `FrameworkElementAutomationPeer` subclass with `GetAutomationControlTypeCore() => AutomationControlType.DataGrid`. Without that peer, a custom `FrameworkElement` is not exposed to UIA (record in DECISIONS.md).

Scrolling: the vertical scrollbar maps to `TopRow`, range 1..100000, and the horizontal one to `LeftCol`, range 1..702. The mouse wheel scrolls 3 rows. Work per frame is O(visible cells).

- [ ] **Step 1: Write failing tests**

```csharp
[Theory] [InlineData(1234567890123.0, "1.234567890E+12")] [InlineData(0.1, "0.1")] [InlineData(3.0, "3")]
public void Number_G10Right(double d, string text) { var (t, a) = CellFormatter.Format(CellValue.FromNumber(d)); Assert.Equal(d.ToString("G10", CultureInfo.InvariantCulture), t); Assert.Equal(TextAlignment.Right, a); }
[Fact] public void Text_Left_Bool_Error_Center() { Assert.Equal(TextAlignment.Left, CellFormatter.Format(CellValue.FromText("x")).Align); Assert.Equal(TextAlignment.Center, CellFormatter.Format(CellValue.FromBoolean(true)).Align); Assert.Equal(("#N/A", TextAlignment.Center), CellFormatter.Format(CellValue.FromError("#N/A"))); }
[Fact] public void Selection_ExtendAndClamp() { var s = new GridSelection(); s.MoveBy(-1, -1, false); Assert.Equal("A1", s.Active.ToString()); s.MoveBy(2, 1, true); Assert.Equal("A1:B3", s.Range.ToString()); s.MoveTo(CellAddress.Parse("ZZ100000"), false); s.MoveBy(1, 1, false); Assert.Equal("ZZ100000", s.Active.ToString()); }
```

The test computes the expected G10 text itself, so the `InlineData` text column is documentation only.

- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement the grid.
- [ ] **Step 4:** Run the tests. Expected: PASS. Then launch the app manually with `dotnet run --project src/Gridline.App -c Release`, drag the scrollbars to row 100000 and column ZZ, and confirm the rendering stays responsive. Commit: `feat(app): virtualized grid control (§11.3)`.

---

### Task 7: Editing, name box, formula bar, status bar (§11.4, §11.7)

**Files:**
- Modify: `GridControl.cs` (keyboard), `MainWindow.xaml(.cs)`.
- Create: an in-cell editor. It is a `TextBox` overlay positioned over the active cell, hosted in the same panel as the grid.

Behaviour:
- Typing a printable character (`TextInput`) starts editing with that character.
- F2 starts editing with the existing input.
- Enter commits and moves down. Tab commits and moves right. Esc cancels.
- Losing focus commits. That includes clicking another cell.
- Arrow keys move the selection; Shift+arrows extend it.
- Delete clears the selection with one `ApplyBatch` that sets every cell in the selection to `""`.
- The name box shows `Selection.Active.ToString()`. On Enter it calls `CellAddress.TryParse`: success navigates and scrolls the cell into view; failure reverts the box text. Either way, focus returns to the grid.
- The formula bar shows `GetInput(active)`. Enter commits through `SetCell`; Esc reverts.
- `SelectionValue` shows `GetValue(active).ToCanonicalString()`.
- `SelectionSum` shows the sum of the Number-kind values in the selection, formatted `G10` invariant. Compute it with the workbook's used addresses that fall inside the range. Do not iterate every position of a huge selection.
- Undo and Redo menu items are enabled per `CanUndo`/`CanRedo`.

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** Run the app manually through this script:
  - Type `5`, Enter. A2 is active.
  - Type `=A1*2`, Tab. B2 is active, and A2 shows `10`.
  - Ctrl+Z clears A2.
  - Typing `$b$7` in the name box navigates to `B7`.
  - Delete over a selection clears it.
- [ ] **Step 3:** Commit: `feat(app): editing, name box, formula bar, status bar (§11.4, §11.7)`. The FlaUI tests for this behaviour live in Task 11.

---

### Task 8: File operations and the unsaved-changes prompt (§11.5)

**Files:**
- Create: `src/Gridline.App/UnsavedChangesDialog.cs`
- Modify: `MainWindow.xaml.cs`

Behaviour:
- New, Open and Exit (including the window close button) check `Workbook.IsDirty` first. When it is dirty, the app shows a modal Save / Don't Save / Cancel dialog with those three buttons:
  - Save saves the file (Save As if the file is untitled). If the user cancels Save As, the original action is aborted.
  - Don't Save proceeds.
  - Cancel aborts.
- Open and Save As use `Microsoft.Win32.OpenFileDialog`/`SaveFileDialog` with the filter `Gridline (*.gridline)|*.gridline`.
- A load failure shows a message box with the error and keeps the current document.

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** Verify manually:
  - Edit, Ctrl+N: the prompt appears; Cancel keeps the edit.
  - Ctrl+S on an untitled document opens Save As.
  - After a save, the title loses its `*`.
- [ ] **Step 3:** Commit: `feat(app): file commands and unsaved-changes prompt (§11.5)`.

---

### Task 9: Clipboard (§11.6)

**Files:**
- Create: `src/Gridline.IO/DelimitedReader.cs`, `src/Gridline.App/ClipboardService.cs`
- Test: `tests/Gridline.IO.Tests/DelimitedReaderTests.cs`

**Interfaces:**
- Produces: `public static class DelimitedReader { static IReadOnlyList<IReadOnlyList<string>> Parse(string text, char delimiter); }`:
  - It implements RFC 4180 per §8.5 "Records" and "Fields".
  - Line breaks may be CRLF, LF or CR, mixed.
  - A final terminator adds no row.
  - A quoted field may hold the delimiter, `""` and newlines.
  - A `"` inside an unquoted field is literal.
  - An unterminated quote throws `GridlineFormatException`.
  - Phase 3 CSV import reuses this reader.
- Produces: `public static class DelimitedMapping { static string ToInput(string field); }`. An empty field gives `""`; a field starting with `=` or `'` gives `"'" + field`; anything else is returned verbatim.
- `ClipboardService`:
  - `Copy(RangeAddress)` stores the internal source range and the clipboard text. The text is the displayed values (`CellFormatter` text) joined by Tab, with CRLF between rows. It goes on the system clipboard via `Clipboard.SetText`.
  - `Paste(CellAddress)`:
    - If the clipboard text still equals the text this app last set, call `Workbook.CopyPaste(source, dest)`.
    - Otherwise `DelimitedReader.Parse(text, '\t')`, then `DelimitedMapping`, then one `ApplyBatch` anchored at the active cell, clipped to the grid.
  - Errors show a status message.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void MixedLineEndings_NoTrailingRow() => Assert.Equal(3, DelimitedReader.Parse("a\r\nb\nc\r", ',').Count);
[Fact] public void QuotedFieldWithDelimiterQuoteNewline() => Assert.Equal("x,\"y\"\nz", DelimitedReader.Parse("\"x,\"\"y\"\"\nz\",2", ',')[0][0]);
[Fact] public void BareQuoteLiteral() => Assert.Equal("a\"b", DelimitedReader.Parse("a\"b", ',')[0][0]);
[Fact] public void Unterminated_Throws() => Assert.Throws<GridlineFormatException>(() => DelimitedReader.Parse("\"abc", ','));
[Fact] public void EmptyFields() => Assert.Equal(new[]{"", "", ""}, DelimitedReader.Parse(",,", ',')[0]);
[Theory] [InlineData("=1+1", "'=1+1")] [InlineData("'x", "''x")] [InlineData("-5", "-5")] [InlineData("", "")]
public void Mapping(string f, string input) => Assert.Equal(input, DelimitedMapping.ToInput(f));
```

- [ ] **Step 2:** Run with `--filter DelimitedReaderTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the reader, the mapping and the service.
- [ ] **Step 4:** Run the tests. Expected: PASS. Then verify manually: copy A1:B2 into Notepad, and paste a tab-separated block from Notepad into the app. Commit: `feat(app,io): clipboard copy/paste with TSV (§11.6)`.

---

### Task 10: Autosave and recovery (§11.6)

**Files:**
- Create: `src/Gridline.App/{RecoveryService,RecoveryDialog.xaml,RecoveryDialog.xaml.cs}.cs`
- Test: `tests/Gridline.App.Tests/RecoveryServiceTests.cs` (non-UI)

**Interfaces:**
- Produces: `public sealed class RecoveryService(string recoveryDir, Guid sessionId)`:
  - `string RecoveryPath`.
  - `void AutosaveIfDirty(Workbook wb)` calls `GridlineFile.SaveToPath` when `wb.IsDirty`.
  - `void Delete()`.
  - `IReadOnlyList<string> FindRecoveryFiles()` returns the `*.gridline` files other than its own.
  - `static string DefaultDir => Path.Combine(Environment.GetFolderPath(LocalApplicationData), "Gridline", "Recovery")`.
- MainWindow behaviour:
  - A `DispatcherTimer` ticks every 60 s and calls `AutosaveIfDirty`.
  - A successful Save/SaveAs, or a clean exit, calls `Delete()`. A clean exit means the user did not cancel.
  - At startup, if `FindRecoveryFiles()` is non-empty, show `RecoveryDialog`. It is a `Window` with `AutomationProperties.AutomationId="RecoveryDialog"`, listing each file with Restore and Discard buttons:
    - Restore opens the file as an untitled dirty document. Load it, then mark it dirty by keeping a `RecoveredFrom` flag that `DocumentSession.Title` treats as dirty until the next save (record in DECISIONS.md). Then delete the recovery file.
    - Discard deletes the file.
  - The recovery dir can be overridden with the environment variable `GRIDLINE_RECOVERY_DIR`, so UI tests can use a temp folder (record in DECISIONS.md).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Autosave_OnlyWhenDirty() { var svc = new RecoveryService(Dir, Guid.NewGuid()); var wb = new Workbook(); svc.AutosaveIfDirty(wb); Assert.False(File.Exists(svc.RecoveryPath)); wb.SetCell("A1","1"); svc.AutosaveIfDirty(wb); Assert.True(File.Exists(svc.RecoveryPath)); }
[Fact] public void Find_ExcludesOwnFile() { /* two services in same dir; each finds only the other's file */ }
[Fact] public void Delete_RemovesFile() { }
```

- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement the service and the dialog.
- [ ] **Step 4:** Run the tests. Expected: PASS. Commit: `feat(app): autosave and crash recovery (§11.6)`.

---

### Task 11: FlaUI UI tests and P-6 (§11.7, §12)

**Files:**
- Test: `tests/Gridline.App.Tests/UiTests/{AppFixture,EditingUiTests,MenuUiTests,RecoveryUiTests,PerfUiTests}.cs`, all `[Trait("Category","UI")]`.

`AppFixture`:
- Launches the built `Gridline.App.exe`, found relative to the test output.
- Accepts an optional file argument. `App.OnStartup` opens `args[0]` if one is present.
- Sets `GRIDLINE_RECOVERY_DIR` to a temp dir.
- Waits for the main window, and kills the app on dispose.
- Finds elements with `ByAutomationId`.

Tests:
- `Title_UntitledThenDirty`: the title is `Gridline - Untitled`; after typing in the grid it becomes `Gridline - Untitled *`.
- `TypeCommitMovesDown`: focus `Grid` and type `5{ENTER}`. The `NameBox` text is `A2`. Navigating back to A1 makes `SelectionValue` show `N:5`.
- `NameBox_AcceptsAbsoluteLowercase`: enter `$b$7` in the name box. Its text becomes `B7`. An invalid `A0` reverts it.
- `FormulaBar_ShowsInputAndCommits`: the formula bar shows the active cell's input. Typing a new input followed by Enter updates `SelectionValue`.
- `SelectionSum`: put 1, 2 and `x` in A1:A3, then Shift+Down twice. `SelectionSum` contains `3`.
- `UndoRedoMenus`: `MenuEdit` → `MenuUndo` reverts the edit; `MenuRedo` reapplies it.
- `Delete_ClearsSelection_OneUndo`.
- `RecoveryDialog_ShownWhenFilesExist`: place a `.gridline` file in the recovery dir before launch. The `RecoveryDialog` window appears.
- `P6_Open100kFile_RenderedWithin3s`: generate the P-1 file and launch with it. Measure from process start until the `SelectionValue` text is `N:…` for A1 (the grid is rendered and the document is loaded). The budget is ≤ 3 s.

- [ ] **Step 1:** Write the tests.
- [ ] **Step 2:** Run `dotnet test tests/Gridline.App.Tests -c Release --filter Category=UI`. Expected: PASS. If a test fails, fix the app, not the test.
- [ ] **Step 3:** Commit: `test(app): FlaUI automation-surface tests and P-6 (§11.7, §12)`.

---

### Task 12: Phase 2 exit (§13)

- [ ] **Step 1:** Run `dotnet build Gridline.sln -c Release`. Expected: 0 errors and 0 warnings.
- [ ] **Step 2:** Run `dotnet test Gridline.sln -c Release --filter "Category!=UI"`. Expected: all tests pass, Phase 1 included.
- [ ] **Step 3:** Run `dotnet test Gridline.sln -c Release --filter Category=UI`. Expected: PASS.
- [ ] **Step 4:** Update `DECISIONS.md`, commit `chore: phase 2 complete (§13)`, and tag `phase-2`.
