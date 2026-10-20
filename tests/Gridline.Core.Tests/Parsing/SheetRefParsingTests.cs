using Gridline.Core.Addressing;
using Gridline.Core.Parsing;

namespace Gridline.Core.Tests.Parsing;

public class SheetRefParsingTests
{
    private sealed class FakeArity : IFunctionArity
    {
        public bool TryGetArity(string upperName, out int min, out int max)
        {
            (min, max) = upperName == "SUM" ? (1, 255) : (-1, -1);
            return min >= 0;
        }
    }

    private static readonly IFunctionArity Arity = new FakeArity();
    private static ParsedFormula Parse(string body) => Parser.Parse(body, Arity);

    [Theory]
    [InlineData("Sheet2!a1", "=Sheet2!A1")]
    [InlineData("'my data'!$A$1:b3+1", "='my data'!$A$1:B3+1")]
    [InlineData("'It''s'!A1", "='It''s'!A1")]
    [InlineData("SUM(Data!A1:A3, A1)", "=SUM(Data!A1:A3,A1)")]
    [InlineData("'Sheet1'!A1", "=Sheet1!A1")]          // quotes dropped when not required
    [InlineData("_x!A1", "=_x!A1")]
    [InlineData(".a!A1", "=.a!A1")]
    [InlineData("'2024'!A1+'A1'!B2+'true'!C3", "='2024'!A1+'A1'!B2+'true'!C3")]
    public void Printer_QuotesOnlyWhenRequired(string body, string printed) =>
        Assert.Equal(printed, FormulaPrinter.Print(Parse(body).Root!, _ => null));

    [Theory]
    [InlineData("Sheet2!A1:Sheet2!B3")] [InlineData("Sheet2! A1")] [InlineData("My Sheet!A1")]
    [InlineData("2024!A1")] [InlineData("A1!B2")] [InlineData("Sheet2!Rate")] [InlineData("''!A1")]
    [InlineData("Sheet2!SUM(A1)")] [InlineData("Sheet2!")] [InlineData("Sheet2!A1:")] [InlineData("Sheet2!1")]
    [InlineData("A1:Sheet2!B3")] [InlineData("'abc")] [InlineData("TRUE!A1")]
    public void Invalid_IsParseError(string body) => Assert.True(Parse(body).IsParseError);

    [Theory]
    [InlineData(".5+1", "=.5+1")]
    [InlineData("#REF!+1", "=#REF!+1")]
    [InlineData("A1:B2", "=A1:B2")]
    public void Unqualified_Unchanged(string body, string printed) =>
        Assert.Equal(printed, FormulaPrinter.Print(Parse(body).Root!, _ => null));

    [Fact] public void Ast_RangeCarriesSheet_InnerRefsUnqualified()
    {
        var rg = Assert.IsType<RangeNode>(Parse("Data!A1:B2").Root);
        Assert.Equal("Data", rg.Sheet);
        Assert.Null(rg.First.Sheet);
        Assert.Null(rg.Second.Sheet);
        Assert.Equal("It's", Assert.IsType<RefNode>(Parse("'It''s'!A1").Root).Sheet);
    }

    [Fact] public void Printer_UsesDefinedSheetCasing() =>
        Assert.Equal("=Data!A1", FormulaPrinter.Print(Parse("DATA!a1").Root!, _ => null, s => s == "DATA" ? "Data" : null));

    [Fact] public void RenameSheet_RewritesAndRefs()
    {
        var r = ReferenceRewriter.RenameSheet(Parse("data!A1+Other!B1+A1").Root!, "Data", "New Data", out bool ch);
        Assert.True(ch); Assert.Equal("='New Data'!A1+Other!B1+A1", FormulaPrinter.Print(r, _ => null));
        r = ReferenceRewriter.RenameSheet(Parse("SUM(Data!A1:B2)").Root!, "data", null, out _);
        Assert.Equal("=SUM(#REF!)", FormulaPrinter.Print(r, _ => null));
    }

    [Fact] public void RenameSheet_NoMatch_Unchanged()
    {
        var r = ReferenceRewriter.RenameSheet(Parse("Other!A1+A1").Root!, "Data", "X", out bool ch);
        Assert.False(ch); Assert.Equal("=Other!A1+A1", FormulaPrinter.Print(r, _ => null));
    }

    [Fact] public void Structural_RespectsSheetPredicate()
    {
        var e = new StructuralEdit(Axis.Row, 1, 1, IsInsert: true);
        var r = ReferenceRewriter.Structural(Parse("A1+Data!A1").Root!, e, s => s is not null, out _);
        Assert.Equal("=A1+Data!A2", FormulaPrinter.Print(r, _ => null));
    }

    [Fact] public void Structural_RangeKeepsSheet()
    {
        var e = new StructuralEdit(Axis.Row, 1, 1, IsInsert: true);
        var r = ReferenceRewriter.Structural(Parse("SUM(Data!B3:A1)").Root!, e, out _);
        Assert.Equal("=SUM(Data!A2:B4)", FormulaPrinter.Print(r, _ => null));
    }

    [Fact] public void Shift_KeepsSheet()
    {
        var r = ReferenceRewriter.Shift(Parse("Data!A1+Data!A1:B2").Root!, 1, 1, out _);
        Assert.Equal("=Data!B2+Data!B2:C3", FormulaPrinter.Print(r, _ => null));
    }
}
