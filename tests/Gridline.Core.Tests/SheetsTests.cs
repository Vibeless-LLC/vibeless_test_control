namespace Gridline.Core.Tests;

/// <summary>Sheet list, active sheet, qualified API addresses and names (CR1.1, CR1.3, CR1.4).</summary>
public class SheetsTests
{
    private static string V(Workbook wb, string a) => wb.GetValue(a).ToCanonicalString();
    private static KeyValuePair<string, string> KV(string k, string v) => KeyValuePair.Create(k, v);

    [Fact]
    public void NewWorkbook_HasSheet1Active()
    {
        var wb = new Workbook();
        Assert.Equal(["Sheet1"], wb.SheetNames);
        Assert.Equal("Sheet1", wb.ActiveSheet);
    }

    [Fact]
    public void AddSheet_AppendsOrInserts_AndActiveIsUnchanged()
    {
        var wb = new Workbook();
        wb.AddSheet("Data");
        wb.AddSheet("First", 0);
        wb.AddSheet("Mid", 2);
        Assert.Equal(["First", "Sheet1", "Mid", "Data"], wb.SheetNames);
        Assert.Equal("Sheet1", wb.ActiveSheet);
    }

    [Fact]
    public void AddSheet_Validation()
    {
        var wb = new Workbook();
        Assert.Throws<ArgumentException>(() => wb.AddSheet("a*b"));
        Assert.Throws<ArgumentException>(() => wb.AddSheet(""));
        Assert.Throws<ArgumentException>(() => wb.AddSheet("'x"));
        Assert.Throws<ArgumentException>(() => wb.AddSheet(new string('x', 32)));
        Assert.Throws<ArgumentException>(() => wb.AddSheet("sheet1"));
        Assert.ThrowsAny<ArgumentException>(() => wb.AddSheet(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.AddSheet("X", 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.AddSheet("X", -1));
        Assert.False(wb.CanUndo);
        for (int i = 2; i <= 255; i++) wb.AddSheet("S" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x");
        Assert.Equal(255, wb.SheetNames.Count);
        Assert.Throws<InvalidOperationException>(() => wb.AddSheet("Overflow"));
        Assert.Equal(255, wb.SheetNames.Count);
    }

    [Fact]
    public void ActiveSheet_Set_IsCaseInsensitive_UnknownThrows()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.ActiveSheet = "data";
        Assert.Equal("Data", wb.ActiveSheet);
        Assert.Throws<ArgumentException>(() => wb.ActiveSheet = "Nope");
        Assert.ThrowsAny<ArgumentException>(() => wb.ActiveSheet = null!);
        Assert.Equal("Data", wb.ActiveSheet);
    }

    [Fact]
    public void AddMove_AreOneUndoStepEach_ActiveIsNot()
    {
        var wb = new Workbook();
        wb.AddSheet("Data");
        Assert.True(wb.CanUndo);
        wb.MarkSaved();
        wb.ActiveSheet = "Data";
        Assert.False(wb.IsDirty);
        wb.MoveSheet("Data", 0);
        Assert.Equal(["Data", "Sheet1"], wb.SheetNames);
        Assert.True(wb.IsDirty);
        Assert.True(wb.Undo());
        Assert.Equal(["Sheet1", "Data"], wb.SheetNames);
        Assert.False(wb.IsDirty);
        Assert.Equal("Data", wb.ActiveSheet);
        Assert.True(wb.Undo());
        Assert.Equal(["Sheet1"], wb.SheetNames);
        Assert.False(wb.CanUndo);
        Assert.True(wb.Redo());
        Assert.True(wb.Redo());
        Assert.Equal(["Data", "Sheet1"], wb.SheetNames);
    }

    [Fact]
    public void MoveSheet_Validation_AndSameIndexIsNoOp()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        Assert.Throws<ArgumentException>(() => wb.MoveSheet("Nope", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.MoveSheet("Data", 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.MoveSheet("Data", -1));
        wb.MarkSaved();
        wb.MoveSheet("data", 1);
        Assert.False(wb.IsDirty);
        wb.MoveSheet("DATA", 0);
        Assert.Equal(["Data", "Sheet1"], wb.SheetNames);
    }

    [Fact]
    public void UndoAddSheet_WhileActive_ActivatesNeighbour()
    {
        var wb = new Workbook(); wb.AddSheet("A2x"); wb.AddSheet("Mid", 1);
        Assert.Equal(["Sheet1", "Mid", "A2x"], wb.SheetNames);
        wb.ActiveSheet = "Mid";
        wb.Undo(); // removes Mid (index 1): the sheet now at index 1 becomes active
        Assert.Equal(["Sheet1", "A2x"], wb.SheetNames);
        Assert.Equal("A2x", wb.ActiveSheet);
        wb.Undo(); // removes A2x (index 1, last): the last sheet becomes active
        Assert.Equal(["Sheet1"], wb.SheetNames);
        Assert.Equal("Sheet1", wb.ActiveSheet);
        wb.Redo(); wb.Redo();
        Assert.Equal(["Sheet1", "Mid", "A2x"], wb.SheetNames);
        Assert.Equal("Sheet1", wb.ActiveSheet);
    }

    [Fact]
    public void RedoAddSheet_ReusesSheetIdentity()
    {
        var wb = new Workbook();
        wb.AddSheet("Data");
        wb.SetCell("Data!A1", "7");
        wb.SetCell("A1", "=Data!A1");
        wb.Undo(); wb.Undo(); wb.Undo();
        Assert.Equal(["Sheet1"], wb.SheetNames);
        wb.Redo(); wb.Redo(); wb.Redo();
        Assert.Equal("7", wb.GetInput("Data!A1"));
        Assert.Equal("N:7", V(wb, "A1"));
        // A new sheet added after an undo gets a fresh identity: it starts empty.
        wb.Undo(); wb.Undo(); wb.Undo();
        wb.AddSheet("Data");
        Assert.Equal("", wb.GetInput("Data!A1"));
        Assert.False(wb.CanRedo);
    }

    [Fact]
    public void QualifiedAddresses_InApi()
    {
        var wb = new Workbook(); wb.AddSheet("My Data");
        wb.SetCell("'My Data'!B2", "5");
        Assert.Equal("5", wb.GetInput("'my data'!b2"));
        Assert.Equal("N:5", V(wb, "'MY DATA'!B2"));
        Assert.Equal("", wb.GetInput("B2"));
        wb.ActiveSheet = "My Data";
        Assert.Equal("5", wb.GetInput("B2"));
        Assert.Equal("", wb.GetInput("Sheet1!B2"));
        Assert.Throws<ArgumentException>(() => wb.SetCell("Nope!A1", "1"));
        Assert.Throws<ArgumentException>(() => wb.GetInput("Nope!A1"));
        Assert.Throws<ArgumentException>(() => wb.GetValue("'Nope'!A1"));
        Assert.Throws<ArgumentException>(() => wb.ApplyBatch([new("A1", "1"), new("Nope!A1", "1")]));
        Assert.Throws<ArgumentException>(() => wb.SetCell("My Data!A1", "1"));
        Assert.Equal("", wb.GetInput("A1"));
    }

    [Fact]
    public void ApplyBatch_AcrossSheets_IsOneStep()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.ApplyBatch([new("A1", "1"), new("Data!A1", "2"), new("sheet1!a1", "3")]);
        Assert.Equal("3", wb.GetInput("A1"));
        Assert.Equal("2", wb.GetInput("Data!A1"));
        wb.Undo();
        Assert.Equal("", wb.GetInput("A1"));
        Assert.Equal("", wb.GetInput("Data!A1"));
    }

    [Fact]
    public void CopyPaste_QualifiedSourceAndDestination()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("A1", "=B1+Data!B1");
        wb.SetCell("A2", "x");
        wb.CopyPaste("Sheet1!A1:A2", "Data!A2");
        Assert.Equal("=B2+Data!B2", wb.GetInput("Data!A2"));
        Assert.Equal("x", wb.GetInput("Data!A3"));
        Assert.Throws<ArgumentException>(() => wb.CopyPaste("Nope!A1", "A1"));
        Assert.Throws<ArgumentException>(() => wb.CopyPaste("A1", "Nope!A1"));
    }

    [Fact]
    public void GetUsedAddresses_SingleVsMulti()
    {
        var wb = new Workbook();
        wb.SetCell("A1", "1");
        Assert.Equal(["A1"], wb.GetUsedAddresses());
        wb.AddSheet("My Data");
        wb.SetCell("'My Data'!B2", "2");
        Assert.Equal(["Sheet1!A1", "'My Data'!B2"], wb.GetUsedAddresses());
        wb.MoveSheet("My Data", 0);
        Assert.Equal(["'My Data'!B2", "Sheet1!A1"], wb.GetUsedAddresses());
        Assert.Equal(["B2"], wb.GetUsedAddresses("my data"));
        Assert.Equal(["A1"], wb.GetUsedAddresses("Sheet1"));
        Assert.Throws<ArgumentException>(() => wb.GetUsedAddresses("Nope"));
    }

    [Fact]
    public void Names_QualifiedWithActiveSheet()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.ActiveSheet = "Data";
        wb.DefineName("Rate", "$B$1");
        Assert.Equal([KV("Rate", "Data!$B$1")], wb.GetNames());
        wb.SetCell("B1", "4");
        wb.SetCell("Sheet1!A1", "=Rate*2");
        Assert.Equal("N:8", V(wb, "Sheet1!A1"));
        wb.SetCell("Data!B1", "5");
        Assert.Equal("N:10", V(wb, "Sheet1!A1"));
    }

    [Fact]
    public void Names_QualifiedRefersTo_DefinedCasing_AndRanges()
    {
        var wb = new Workbook(); wb.AddSheet("My Data");
        wb.DefineName("Tot", "'my data'!$a$1:$a$3");
        wb.DefineName("One", "sheet1!$C$1");
        Assert.Equal([KV("One", "Sheet1!$C$1"), KV("Tot", "'My Data'!$A$1:$A$3")], wb.GetNames());
        wb.ApplyBatch([new("'My Data'!A1", "1"), new("'My Data'!A2", "2"), new("C1", "10")]);
        wb.SetCell("B1", "=SUM(Tot)+One");
        Assert.Equal("N:13", V(wb, "B1"));
    }

    [Fact]
    public void Names_UnknownSheet_Throws()
    {
        var wb = new Workbook();
        Assert.Throws<ArgumentException>(() => wb.DefineName("Rate", "Nope!$B$1"));
        Assert.Throws<ArgumentException>(() => wb.DefineName("Rate", "Sheet1!B1"));
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot([], [KV("Rate", "Data!$A$1")]));
        Assert.Empty(wb.GetNames());
        Assert.False(wb.CanUndo);
    }

    [Fact]
    public void FromSnapshot_AcceptsQualifiedOrUnqualifiedRefersTo()
    {
        var wb = Workbook.FromSnapshot([KV("A1", "2"), KV("B1", "=Rate+Other")], [KV("Rate", "$A$1"), KV("Other", "SHEET1!$A$1")]);
        Assert.Equal([KV("Other", "Sheet1!$A$1"), KV("Rate", "Sheet1!$A$1")], wb.GetNames());
        Assert.Equal("N:4", V(wb, "B1"));
        Assert.Equal(["Sheet1"], wb.SheetNames);
    }

    [Fact]
    public void SheetsChanged_RaisedForAddMoveActivateUndo()
    {
        var wb = new Workbook();
        int n = 0;
        wb.SheetsChanged += (_, _) => n++;
        wb.AddSheet("Data"); Assert.Equal(1, n);
        wb.MoveSheet("Data", 0); Assert.Equal(2, n);
        wb.ActiveSheet = "Data"; Assert.Equal(3, n);
        wb.ActiveSheet = "data"; Assert.Equal(3, n); // unchanged: no event
        wb.Undo(); Assert.Equal(4, n);
        wb.Redo(); Assert.Equal(5, n);
        wb.SetCell("A1", "1"); Assert.Equal(5, n);
        wb.MoveSheet("Data", 0); Assert.Equal(5, n); // no-op
    }

    [Fact]
    public void StructuralEdit_ActsOnActiveSheetOnly()
    {
        var wb = new Workbook(); wb.AddSheet("Data");
        wb.SetCell("A1", "1");
        wb.SetCell("Data!A1", "2");
        wb.ActiveSheet = "Data";
        wb.InsertRows(1, 1);
        Assert.Equal("2", wb.GetInput("Data!A2"));
        Assert.Equal("1", wb.GetInput("Sheet1!A1"));
        wb.Undo();
        Assert.Equal("2", wb.GetInput("Data!A1"));
    }
}
