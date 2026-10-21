using static Gridline.Cli.Tests.CliAppTests;

namespace Gridline.Cli.Tests;

/// <summary>Sheet commands and qualified addresses in the script language (CR1.7, §10.3).</summary>
public class ScriptSheetTests
{
    private static (int code, string stdout) S(string script)
    {
        var r = Run(script, "script");
        return (r.code, r.stdout);
    }

    [Fact]
    public void Script_SheetCommands()
    {
        var r = S("ADDSHEET 'My Data'\nADDSHEET Zed 0\nSHEETS\nACTIVATE 'my data'\nSHEETS\n");
        Assert.Equal(0, r.code);
        Assert.Equal(
            "SHEET\t0\tZed\nSHEET\t1\tSheet1\nSHEET\t2\tMy Data\nACTIVE\tSheet1\n" +
            "SHEET\t0\tZed\nSHEET\t1\tSheet1\nSHEET\t2\tMy Data\nACTIVE\tMy Data\n", r.stdout);
    }

    [Fact]
    public void Script_QualifiedGetSet()
    {
        var r = S("ADDSHEET 'My Data'\nSET 'My Data'!a1 5\nSET A1 ='my data'!A1*2\nGET 'MY DATA'!A1\nGET A1\nINPUT 'my data'!A1\nGET sheet1!a1\n");
        Assert.Equal(0, r.code);
        Assert.Equal("'My Data'!A1\tN:5\nA1\tN:10\n'My Data'!A1\t5\nSheet1!A1\tN:10\n", r.stdout);
    }

    [Fact]
    public void Script_QualifiedSetx_AndBatch()
    {
        var r = S("ADDSHEET Data\nSETX data!A1 a\\tb\nBEGIN\nSET 'Data'!B1 7\nSET B1 =Data!B1+1\nEND\nINPUT Data!A1\nGET B1\nGET DATA!B1\n");
        Assert.Equal(0, r.code);
        Assert.Equal("Data!A1\ta\\tb\nB1\tN:8\nData!B1\tN:7\n", r.stdout);
    }

    [Fact]
    public void Script_Batch_UnknownSheet_ErrAtEnd_NothingApplied()
    {
        var r = S("BEGIN\nSET A1 1\nSET Nope!A1 2\nEND\nGET A1\n");
        Assert.Equal(4, r.code);
        var lines = r.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("ERR\t", lines[0]);
        Assert.Equal("A1\tBLANK", lines[1]);
    }

    [Fact]
    public void Script_QualifiedPaste()
    {
        var r = S("ADDSHEET 'My Data'\nSET 'My Data'!A1 =B1+1\nSET 'My Data'!B1 4\nPASTE 'My Data'!A1 Sheet1!C3\nINPUT C3\n");
        Assert.Equal(0, r.code);
        Assert.Equal("C3\t=D3+1\n", r.stdout);
    }

    [Fact]
    public void Script_QuotedSheetArgs()
    {
        var r = S("ADDSHEET 'My Data'\nSET A1 ='my data'!A1*2\nRENAMESHEET 'My Data' 'It''s'\nSHEETS\nINSERTCOLS 'It''s'!A 2\nINPUT A1\nADDSHEET 'bad*'\n");
        Assert.Equal(4, r.code);
        Assert.Contains("SHEET\t1\tIt's\n", r.stdout);
        Assert.Contains("A1\t='It''s'!C1*2\n", r.stdout);
        Assert.EndsWith("\n", r.stdout);
        var lines = r.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("ERR\t", lines[^1]);
        Assert.Single(lines, l => l.StartsWith("ERR\t", StringComparison.Ordinal));
    }

    [Fact]
    public void Script_StructureOnOtherSheet_LeavesActiveAlone()
    {
        var r = S("ADDSHEET Data\nSET Data!A1 1\nSET A1 2\nINSERTROWS Data!1 2\nDELETECOLS Sheet1!A 1\nGET Data!A3\nGET A1\nDUMP\n");
        Assert.Equal(0, r.code);
        Assert.Equal("Data!A3\tN:1\nA1\tBLANK\nData!A3\tN:1\n", r.stdout);
    }

    [Fact]
    public void Script_MoveDeleteActivate()
    {
        var r = S("ADDSHEET A\nADDSHEET B\nMOVESHEET b 0\nACTIVATE A\nDELETESHEET a\nSHEETS\n");
        Assert.Equal(0, r.code);
        Assert.Equal("SHEET\t0\tB\nSHEET\t1\tSheet1\nACTIVE\tSheet1\n", r.stdout);
    }

    [Fact]
    public void Script_Dump_Multi()
    {
        var r = S("SET B1 2\nSET A1 1\nADDSHEET 'My Data'\nADDSHEET Zed 0\nSET 'My Data'!A2 =Sheet1!A1+1\nSET Zed!C1 x\nDUMP\n");
        Assert.Equal(0, r.code);
        Assert.Equal("Zed!C1\tT:x\nSheet1!A1\tN:1\nSheet1!B1\tN:2\n'My Data'!A2\tN:2\n", r.stdout);
    }

    [Fact]
    public void Script_Dump_SingleSheet_Unqualified() =>
        Assert.Equal("A1\tN:1\n", S("SET A1 1\nDUMP\n").stdout);

    [Fact]
    public void Script_DeleteLastSheet_Err()
    {
        var r = S("DELETESHEET Sheet1\nSHEETS\n");
        Assert.Equal(4, r.code);
        var lines = r.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("ERR\t", lines[0]);
        Assert.Equal(["SHEET\t0\tSheet1", "ACTIVE\tSheet1"], lines[1..]);
    }

    [Theory]
    [InlineData("GET Nope!A1\n")] [InlineData("INPUT 'No Such'!A1\n")] [InlineData("SET Nope!A1 1\n")]
    [InlineData("ACTIVATE Nope\n")] [InlineData("DELETESHEET Nope\n")] [InlineData("RENAMESHEET Nope X\n")]
    [InlineData("MOVESHEET Sheet1 1\n")] [InlineData("ADDSHEET sheet1\n")] [InlineData("ADDSHEET X 2\n")]
    [InlineData("INSERTROWS Nope!1 1\n")] [InlineData("GET 'Sheet1!A1\n")] [InlineData("GET Sheet1!!A1\n")]
    [InlineData("ADDSHEET\n")] [InlineData("ADDSHEET 'My Data\n")] [InlineData("ADDSHEET My Data\n")] [InlineData("ADDSHEET A1\n")]
    [InlineData("ADDSHEET X -1\n")] [InlineData("ADDSHEET X 1 2\n")] [InlineData("RENAMESHEET Sheet1\n")]
    [InlineData("DELETESHEET\n")] [InlineData("MOVESHEET Sheet1\n")] [InlineData("MOVESHEET Sheet1 x\n")]
    [InlineData("ACTIVATE\n")] [InlineData("ACTIVATE a b\n")] [InlineData("SHEETS x\n")]
    [InlineData("INSERTCOLS 'It''s!A 1\n")] [InlineData("INSERTCOLS Sheet1!1 1\n")] [InlineData("PASTE 'a b A1\n")]
    [InlineData("BEGIN\nADDSHEET X\nEND\n")]
    public void BadSheetCommands_Err(string script)
    {
        var r = S(script);
        Assert.Equal(4, r.code);
        var lines = r.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, l => l.StartsWith("ERR\t", StringComparison.Ordinal));
        Assert.All(lines, l => Assert.StartsWith("ERR\t", l));
    }

    [Theory]
    [InlineData("ADDSHEET\n", "ERR\tusage: ADDSHEET <name> [index]\n")]
    [InlineData("RENAMESHEET a\n", "ERR\tusage: RENAMESHEET <old> <new>\n")]
    [InlineData("DELETESHEET\n", "ERR\tusage: DELETESHEET <name>\n")]
    [InlineData("MOVESHEET a\n", "ERR\tusage: MOVESHEET <name> <index>\n")]
    [InlineData("ACTIVATE\n", "ERR\tusage: ACTIVATE <name>\n")]
    [InlineData("SHEETS 1\n", "ERR\tusage: SHEETS\n")]
    public void UsageMessages(string script, string expected) => Assert.Equal(expected, S(script).stdout);

    [Fact]
    public void Sheets_CommandWordCaseInsensitive_AndSheetsUndoable()
    {
        var r = S("addsheet X\nUNDO\nsheets\n");
        Assert.Equal("OK\nSHEET\t0\tSheet1\nACTIVE\tSheet1\n", r.stdout);
    }

    [Fact]
    public void Sheets_EscapesControlCharacters()
    {
        // A sheet name may hold a tab; SHEETS output stays one line per sheet.
        var r = S("ADDSHEET 'a\tb'\nACTIVATE 'a\tb'\nSHEETS\n");
        Assert.Equal("SHEET\t0\tSheet1\nSHEET\t1\ta\\tb\nACTIVE\ta\\tb\n", r.stdout);
    }

    [Fact]
    public void Name_AcceptsQuotedRefersTo()
    {
        var r = S("ADDSHEET 'My Data'\nNAME rate 'My Data'!$A$1\nSET 'My Data'!A1 3\nSET A1 =rate*2\nNAMES\nGET A1\n");
        Assert.Equal(0, r.code);
        Assert.Equal("NAME\trate\t'My Data'!$A$1\nA1\tN:6\n", r.stdout);
    }
}
