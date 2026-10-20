using static Gridline.Cli.Tests.CliAppTests;

namespace Gridline.Cli.Tests;

/// <summary>INSERTROWS/DELETEROWS/INSERTCOLS/DELETECOLS script commands (§10.3, §9.5).</summary>
public class ScriptStructureTests
{
    [Fact] public void ScriptStructure() => Assert.Equal("C1\t=SUM(A1:B1)\n", Run("SET B1 =SUM(A1:A1)\nINSERTCOLS A 1\nDELETECOLS a 1\nSET C1 =SUM(A1:B1)\nINSERTROWS 5 2\nDELETEROWS 5 2\nINPUT C1\n","script").stdout);

    [Fact] public void Commands_MoveCellsAndAdjust() =>
        Assert.Equal("D3\t=A3*2\nD3\tN:10\nOK\nOK\nB1\t=A1*2\n",
            Run("SET A1 5\nSET B1 =A1*2\ninsertrows 1 2\nInsertCols b 2\nINPUT D3\nGET D3\nUNDO\nUNDO\nINPUT B1\n", "script").stdout);

    [Theory]
    [InlineData("INSERTROWS\n")] [InlineData("INSERTROWS 1\n")] [InlineData("INSERTROWS 0 1\n")] [InlineData("INSERTROWS 1 0\n")]
    [InlineData("INSERTROWS x 1\n")] [InlineData("INSERTROWS 1 1 1\n")] [InlineData("DELETEROWS 99999 3\n")] [InlineData("DELETEROWS 1 -1\n")]
    [InlineData("INSERTCOLS 1 1\n")] [InlineData("INSERTCOLS AAA 1\n")] [InlineData("DELETECOLS ZZ 2\n")] [InlineData("INSERTCOLS A x\n")]
    [InlineData("DELETECOLS\n")] [InlineData("INSERTROWS 1 99999999999\n")] [InlineData("SET A100000 x\nINSERTROWS 1 1\n")]
    public void BadArgs_Err(string script)
    {
        var r = Run(script, "script");
        Assert.Equal(4, r.code);
        Assert.StartsWith("ERR\t", r.stdout);
        Assert.Single(r.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact] public void Structure_NotAllowedInsideBatch() =>
        Assert.StartsWith("ERR\t", Run("BEGIN\nINSERTROWS 1 1\nEND\n", "script").stdout);
}
