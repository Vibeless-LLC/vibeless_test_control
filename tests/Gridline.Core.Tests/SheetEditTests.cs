namespace Gridline.Core.Tests;

/// <summary>RenameSheet, DeleteSheet, sheet-scoped structure edits and cross-sheet paste (CR1.3).</summary>
public class SheetEditTests
{
    private static string V(Workbook wb, string a) => wb.GetValue(a).ToCanonicalString();

    [Fact]
    public void RenameSheet_RewritesFormulasAndNames_UndoVerbatim()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("A1", "= data!a1 + 1"); wb.SetCell("Data!A1", "1"); wb.DefineName("Rate", "Data!$B$1");
        wb.RenameSheet("Data", "My Data");
        Assert.Equal("='My Data'!A1+1", wb.GetInput("A1"));
        Assert.Equal(new KeyValuePair<string, string>("Rate", "'My Data'!$B$1"), wb.GetNames()[0]);
        Assert.Equal("N:2", V(wb, "A1"));
        Assert.Equal(["Sheet1", "My Data"], wb.SheetNames);
        wb.Undo(); Assert.Equal("= data!a1 + 1", wb.GetInput("A1")); Assert.Equal(["Sheet1", "Data"], wb.SheetNames);
        Assert.Equal("Data!$B$1", wb.GetNames()[0].Value);
        Assert.Equal("N:2", V(wb, "A1"));
        wb.Redo(); Assert.Equal("='My Data'!A1+1", wb.GetInput("A1")); Assert.Equal(["Sheet1", "My Data"], wb.SheetNames);
    }

    [Fact]
    public void RenameSheet_ToDanglingName_ResolvesThoseRefs()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("Data!A1", "5");
        wb.SetCell("A1", "=Later!A1");
        wb.SetCell("A2", "=Data!A1");
        Assert.Equal("E:#REF!", V(wb, "A1"));
        wb.RenameSheet("Data", "Later");
        Assert.Equal("=Later!A1", wb.GetInput("A1"));
        Assert.Equal("=Later!A1", wb.GetInput("A2"));
        Assert.Equal("N:5", V(wb, "A1"));
        Assert.Equal("N:5", V(wb, "A2"));
        wb.SetCell("Later!A1", "6");
        Assert.Equal("N:6", V(wb, "A1"));
        wb.Undo(); wb.Undo();
        Assert.Equal("E:#REF!", V(wb, "A1"));
        Assert.Equal("N:5", V(wb, "A2"));
    }

    [Fact]
    public void RenameSheet_Validation_AndNoOps()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        Assert.Throws<ArgumentException>(() => wb.RenameSheet("Nope", "X"));
        Assert.Throws<ArgumentException>(() => wb.RenameSheet("Data", "a*b"));
        Assert.Throws<ArgumentException>(() => wb.RenameSheet("Data", ""));
        Assert.Throws<ArgumentException>(() => wb.RenameSheet("Data", "sheet1"));
        Assert.ThrowsAny<ArgumentException>(() => wb.RenameSheet("Data", null!));
        Assert.ThrowsAny<ArgumentException>(() => wb.RenameSheet(null!, "X"));
        int steps = 0; wb.SheetsChanged += (_, _) => steps++;
        bool couldUndo = wb.CanUndo;
        wb.RenameSheet("Data", "Data");
        Assert.Equal(0, steps);
        wb.Undo(); // undoes AddSheet, not a no-op rename
        Assert.Equal(["Sheet1"], wb.SheetNames);
        Assert.True(couldUndo);
    }

    [Fact]
    public void RenameSheet_CaseOnly_IsAllowedAndReemits()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("A1", "=data!A1");
        wb.RenameSheet("data", "DATA");
        Assert.Equal(["Sheet1", "DATA"], wb.SheetNames);
        Assert.Equal("=DATA!A1", wb.GetInput("A1"));
        wb.Undo();
        Assert.Equal(["Sheet1", "Data"], wb.SheetNames);
        Assert.Equal("=data!A1", wb.GetInput("A1"));
    }

    [Fact]
    public void RenameSheet_ActiveSheetFollowsAndIsOneStep()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.ActiveSheet = "Data";
        wb.SetCell("A1", "=Data!B1"); wb.SetCell("B1", "3");
        wb.RenameSheet("Data", "Other");
        Assert.Equal("Other", wb.ActiveSheet);
        Assert.Equal("=Other!B1", wb.GetInput("A1"));
        Assert.Equal("N:3", V(wb, "A1"));
        wb.Undo();
        Assert.Equal("Data", wb.ActiveSheet);
        Assert.Equal("=Data!B1", wb.GetInput("A1"));
    }

    [Fact]
    public void RenameSheet_RewriteTooLong_ThrowsWithNoChange()
    {
        var wb = new Workbook(); wb.AddSheet("D");
        // Renaming to a 31-char name grows each of the 1100 refs by 30 characters: 5,500 → 38,500.
        string f = "=" + string.Join("+", Enumerable.Repeat("D!A1", 1100));
        Assert.True(f.Length <= Workbook.MaxInputLength);
        wb.SetCell("A1", f);
        Assert.Throws<InvalidOperationException>(() => wb.RenameSheet("D", new string('x', 31)));
        Assert.Equal(["Sheet1", "D"], wb.SheetNames);
        Assert.Equal(f, wb.GetInput("A1"));
    }

    [Fact]
    public void DeleteSheet_UndoRestoresVerbatimNamesAndPosition()
    {
        var wb = new Workbook(); wb.AddSheet("Data", 0); wb.SetCell("Data!A1", "7");
        wb.SetCell("A1", "= data!a1 + 1"); wb.DefineName("Rate", "Data!$A$1");
        wb.DeleteSheet("Data");
        Assert.Equal("=#REF!+1", wb.GetInput("A1")); Assert.Equal("#REF!", wb.GetNames()[0].Value);
        Assert.Equal("E:#REF!", V(wb, "A1"));
        Assert.Equal(["Sheet1"], wb.SheetNames);
        wb.Undo();
        Assert.Equal(["Data", "Sheet1"], wb.SheetNames); Assert.Equal("= data!a1 + 1", wb.GetInput("Sheet1!A1"));
        Assert.Equal("Data!$A$1", wb.GetNames()[0].Value); Assert.Equal("N:8", V(wb, "Sheet1!A1"));
        Assert.Equal("7", wb.GetInput("Data!A1"));
        wb.Redo();
        Assert.Equal(["Sheet1"], wb.SheetNames);
        Assert.Equal("=#REF!+1", wb.GetInput("A1"));
    }

    [Fact]
    public void DeleteSheet_ThenRecreate_DoesNotResurrectRewrittenRefs()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("Data!A1", "1");
        wb.SetCell("A1", "=Data!A1"); wb.SetCell("A2", "=SUM(Data!A1:A3)");
        wb.DeleteSheet("Data");
        Assert.Equal("=#REF!", wb.GetInput("A1"));
        Assert.Equal("=SUM(#REF!)", wb.GetInput("A2"));
        wb.AddSheet("Data");
        Assert.Equal("", wb.GetInput("Data!A1"));
        Assert.Equal("E:#REF!", V(wb, "A1"));
    }

    [Fact]
    public void DeleteSheet_Unknown_Throws()
    {
        var wb = new Workbook();
        Assert.Throws<ArgumentException>(() => wb.DeleteSheet("Nope"));
        Assert.ThrowsAny<ArgumentException>(() => wb.DeleteSheet(null!));
    }

    [Fact]
    public void DeleteLastSheet_Throws()
    {
        var wb = new Workbook(); wb.SetCell("A1", "1");
        Assert.Throws<InvalidOperationException>(() => wb.DeleteSheet("Sheet1"));
        Assert.Equal(["Sheet1"], wb.SheetNames);
        Assert.Equal("1", wb.GetInput("A1"));
        wb.Undo();
        Assert.False(wb.CanUndo);
    }

    [Fact]
    public void DeleteActiveSheet_ActivatesNeighbour()
    {
        var wb = new Workbook(); wb.AddSheet("Data"); wb.AddSheet("S3");
        wb.ActiveSheet = "Data";
        wb.DeleteSheet("Data");
        Assert.Equal("S3", wb.ActiveSheet);
        wb.DeleteSheet("s3");
        Assert.Equal("Sheet1", wb.ActiveSheet);
        Assert.Equal(["Sheet1"], wb.SheetNames);
    }

    [Fact]
    public void DeleteSheet_IsOneStepRaisingCellsChanged()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("Data!A1", "1"); wb.SetCell("A1", "=Data!A1");
        var seen = new List<string[]>();
        wb.CellsChanged += (_, e) => seen.Add(e.Addresses.ToArray());
        wb.DeleteSheet("Data");
        Assert.Equal(["A1"], Assert.Single(seen));
        wb.Undo();
        Assert.True(wb.Undo()); // SetCell A1
        Assert.Equal("", wb.GetInput("A1"));
    }

    [Fact]
    public void InsertRows_OnSheet_AdjustsOnlyThatSheetsReferences()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("A1", "=A5+Data!A5"); wb.SetCell("Data!B1", "=A5+Sheet1!A5");
        wb.InsertRows("Data", 2, 3);
        Assert.Equal("=A5+Data!A8", wb.GetInput("A1"));
        Assert.Equal("=A8+Sheet1!A5", wb.GetInput("Data!B1"));
        wb.Undo();
        Assert.Equal("=A5+Data!A5", wb.GetInput("A1"));
        Assert.Equal("=A5+Sheet1!A5", wb.GetInput("Data!B1"));
    }

    [Fact]
    public void StructuralEdits_OnSheet_MoveOnlyThatSheetsCellsAndNames()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("A3", "1"); wb.SetCell("Data!A3", "2"); wb.SetCell("Data!C1", "x");
        wb.DefineName("Here", "Sheet1!$A$3"); wb.DefineName("There", "Data!$A$3");
        wb.SetCell("B1", "=There*10");
        wb.InsertRows("Data", 1, 2);
        Assert.Equal("1", wb.GetInput("A3"));
        Assert.Equal("2", wb.GetInput("Data!A5"));
        Assert.Equal("", wb.GetInput("Data!A3"));
        Assert.Equal([KeyValuePair.Create("Here", "Sheet1!$A$3"), KeyValuePair.Create("There", "Data!$A$5")], wb.GetNames());
        Assert.Equal("N:20", V(wb, "B1"));

        wb.DeleteColumns("data", 1, 1);
        Assert.Equal("", wb.GetInput("Data!A5"));
        Assert.Equal("x", wb.GetInput("Data!B3"));
        Assert.Equal("#REF!", wb.GetNames()[1].Value);
        Assert.Equal("E:#REF!", V(wb, "B1"));
        Assert.Equal("1", wb.GetInput("A3"));

        wb.InsertColumns("Data", 1, 1);
        Assert.Equal("x", wb.GetInput("Data!C3"));
        wb.DeleteRows("Data", 1, 2);
        Assert.Equal("x", wb.GetInput("Data!C1"));
        Assert.Equal("1", wb.GetInput("A3"));
    }

    [Fact]
    public void StructuralEdits_OnSheet_RewriteOtherSheetsQualifiedRefs()
    {
        var wb = new Workbook(); wb.AddSheet("My Data"); wb.AddSheet("Other");
        wb.SetCell("'My Data'!C3", "4");
        wb.SetCell("A1", "=SUM('my data'!B2:D4)+'My Data'!C3+C3");
        wb.SetCell("Other!A1", "='My Data'!C3*2");
        wb.DeleteColumns("My Data", 1, 1);
        Assert.Equal("=SUM('My Data'!A2:C4)+'My Data'!B3+C3", wb.GetInput("A1"));
        Assert.Equal("='My Data'!B3*2", wb.GetInput("Other!A1"));
        Assert.Equal("N:8", V(wb, "A1"));
        Assert.Equal("N:8", V(wb, "Other!A1"));
        wb.DeleteRows("My Data", 3, 1);
        Assert.Equal("=#REF!*2", wb.GetInput("Other!A1"));
        wb.Undo(); wb.Undo();
        Assert.Equal("=SUM('my data'!B2:D4)+'My Data'!C3+C3", wb.GetInput("A1"));
        Assert.Equal("='My Data'!C3*2", wb.GetInput("Other!A1"));
    }

    [Fact]
    public void StructuralEdits_OnSheet_Validation()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        Assert.Throws<ArgumentException>(() => wb.InsertRows("Nope", 1, 1));
        Assert.Throws<ArgumentException>(() => wb.DeleteColumns("Nope", 1, 1));
        Assert.ThrowsAny<ArgumentException>(() => wb.InsertColumns(null!, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.DeleteRows("Data", 0, 1));
        // Overflow is checked on the target sheet only.
        wb.SetCell("A100000", "1");
        wb.InsertRows("Data", 1, 1);
        Assert.Throws<InvalidOperationException>(() => wb.InsertRows("Sheet1", 1, 1));
        wb.SetCell("Data!ZZ1", "1");
        Assert.Throws<InvalidOperationException>(() => wb.InsertColumns("Data", 1, 1));
        wb.InsertColumns("Sheet1", 1, 1);
        Assert.Equal("1", wb.GetInput("B100000"));
    }

    [Fact]
    public void UnqualifiedStructuralOverloads_ActOnActiveSheet()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("A1", "=Data!A1"); wb.SetCell("Data!A1", "1");
        wb.ActiveSheet = "Data";
        wb.InsertRows(1, 1);
        Assert.Equal("1", wb.GetInput("Data!A2"));
        Assert.Equal("=Data!A2", wb.GetInput("Sheet1!A1"));
    }

    [Fact]
    public void CopyPaste_AcrossSheets_UnqualifiedFollowDestination()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("A1", "=B1+Data!B1");
        wb.CopyPaste("Sheet1!A1", "Data!A2");
        Assert.Equal("=B2+Data!B2", wb.GetInput("Data!A2"));
    }
}
