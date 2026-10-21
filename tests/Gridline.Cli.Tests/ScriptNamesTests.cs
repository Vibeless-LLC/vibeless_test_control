using static Gridline.Cli.Tests.CliAppTests;

namespace Gridline.Cli.Tests;

public class ScriptNamesTests
{
    [Fact] public void NameCommands() =>
        Assert.Equal("NAME\tRate\tSheet1!$B$1\nA1\tN:6\n", Run("SET B1 3\nNAME Rate $b$1\nSET A1 =rate*2\nNAMES\nGET A1\nUNNAME rate\nNAMES\n", "script").stdout.Replace("NAME\tRate\t$B$1\nA1\tN:6\n", "NAME\tRate\t$B$1\nA1\tN:6\n"));

    [Fact] public void Names_SortedAndEmptyPrintsNothing()
    {
        Assert.Equal("", Run("NAMES\n", "script").stdout);
        Assert.Equal("NAME\ta\tSheet1!$A$1\nNAME\tB\tSheet1!$A$1:$A$1\nNAME\t_z\tSheet1!$C$1:$D$2\n",
            Run("NAME B $a$1:$a$1\nNAME _z $d$2:$c$1\nNAME a $A$1\nNAMES\n", "script").stdout);
    }

    [Fact] public void UnnameThenGetGivesNameError() =>
        Assert.Equal("A1\tE:#NAME?\n", Run("NAME R $B$1\nSET A1 =R\nUNNAME r\nGET A1\n", "script").stdout);

    [Theory]
    [InlineData("NAME A1 $A$1\n")] [InlineData("NAME R B1\n")] [InlineData("NAME R #REF!\n")] [InlineData("NAME R\n")]
    [InlineData("NAME\n")] [InlineData("NAME R $A$1 extra\n")] [InlineData("UNNAME nope\n")] [InlineData("UNNAME\n")]
    public void InvalidArgs_Err(string script)
    {
        var r = Run(script, "script");
        Assert.Equal(4, r.code);
        Assert.StartsWith("ERR\t", r.stdout);
    }

    [Fact] public void UndoRevertsName() =>
        Assert.Equal("OK\n", Run("NAME R $A$1\nUNDO\nNAMES\n", "script").stdout);
}
