namespace Gridline.Core.Tests.Functions;

/// <summary>Lookup and reference functions (§7.4).</summary>
public class LookupFunctionTests
{
    private static readonly (string, string)[] Fixture =
    [
        ("A1", "1"), ("B1", "a"), ("A2", "3"), ("B2", "b"), ("A3", "5"), ("B3", "c"), ("A4", "7"), ("B4", "d"),
        ("D1", "1"), ("E1", "3"), ("F1", "5"), ("G1", "7"),
        ("D2", "x"), ("E2", "y"), ("F2", "z"), ("G2", "w"),
        ("H1", "9"), ("H2", "5"), ("H3", "1"),
    ];

    [Theory]
    [InlineData("VLOOKUP(4,A1:B4,2)", "T:b")] [InlineData("VLOOKUP(0,A1:B4,2)", "E:#N/A")] [InlineData("VLOOKUP(9,A1:B4,2,TRUE)", "T:d")] [InlineData("VLOOKUP(4,A1:B4,2,FALSE)", "E:#N/A")]
    [InlineData("VLOOKUP(5,A1:B4,0)", "E:#VALUE!")] [InlineData("VLOOKUP(5,A1:B4,3)", "E:#REF!")] [InlineData("VLOOKUP(\"C\",B1:B4,1,FALSE)", "T:c")] [InlineData("VLOOKUP(\"?\",B1:B4,1,FALSE)", "T:a")]
    [InlineData("HLOOKUP(3,D1:G2,2)", "T:y")]
    [InlineData("INDEX(A1:B4,2,2)", "T:b")] [InlineData("INDEX(B1:B4,3)", "T:c")] [InlineData("INDEX(A1:B4,2)", "E:#VALUE!")] [InlineData("INDEX(A1:B4,0,1)", "E:#VALUE!")] [InlineData("INDEX(A1:B4,5,1)", "E:#REF!")] [InlineData("INDEX(D1:G1,3)", "N:5")]
    [InlineData("MATCH(5,A1:A4,0)", "N:3")] [InlineData("MATCH(6,A1:A4)", "N:3")] [InlineData("MATCH(\"b*\",B1:B4,0)", "N:2")] [InlineData("MATCH(1,A1:B4,0)", "E:#N/A")] [InlineData("MATCH(0,A1:A4)", "E:#N/A")] [InlineData("MATCH(4,H1:H3,-1)", "N:2")]
    [InlineData("CHOOSE(2,1/0,\"x\")", "T:x")] [InlineData("CHOOSE(3,1,2)", "E:#VALUE!")] [InlineData("CHOOSE(1.9,\"a\",\"b\")", "T:a")] [InlineData("CHOOSE(1,A1:A2)", "E:#VALUE!")]
    [InlineData("ROW(C7)", "N:7")] [InlineData("COLUMN(C7:D9)", "N:3")] [InlineData("ROWS(A1:B4)", "N:4")] [InlineData("COLUMNS(A1:B4)", "N:2")]
    // additional edges
    [InlineData("VLOOKUP(1/0,A1:B4,2)", "E:#DIV/0!")] [InlineData("VLOOKUP(5,7,1)", "E:#VALUE!")] [InlineData("VLOOKUP(5,A1:B4,)", "E:#VALUE!")]
    [InlineData("VLOOKUP(TRUE,A1:B4,1,FALSE)", "E:#N/A")] [InlineData("VLOOKUP(\"a\",A1:B4,1,FALSE)", "E:#N/A")] [InlineData("VLOOKUP(5,A1:B4,2.9,0)", "T:c")]
    [InlineData("HLOOKUP(\"z\",D2:G2,1,FALSE)", "T:z")] [InlineData("HLOOKUP(4,D1:G2,3)", "E:#REF!")] [InlineData("HLOOKUP(0,D1:G2,2)", "E:#N/A")]
    [InlineData("INDEX(A1:B4,1,3)", "E:#REF!")] [InlineData("INDEX(A1:B4,-1,1)", "E:#VALUE!")] [InlineData("INDEX(5,1)", "E:#VALUE!")] [InlineData("INDEX(A1,1)", "N:1")]
    [InlineData("INDEX(D1:G1,1,2)", "N:3")] [InlineData("INDEX(A1:A4,1,0)", "E:#VALUE!")]
    [InlineData("MATCH(7,A1:A4,1)", "N:4")] [InlineData("MATCH(9,H1:H3,-1)", "N:1")] [InlineData("MATCH(10,H1:H3,-1)", "E:#N/A")] [InlineData("MATCH(3,D1:G1,0)", "N:2")] [InlineData("MATCH(\"X\",D2:G2,0)", "N:1")]
    [InlineData("MATCH(1/0,A1:A4)", "E:#DIV/0!")] [InlineData("MATCH(1,5,0)", "E:#VALUE!")] [InlineData("MATCH(TRUE,A1:A4,0)", "E:#N/A")]
    [InlineData("CHOOSE(0,1)", "E:#VALUE!")] [InlineData("CHOOSE(\"a\",1)", "E:#VALUE!")] [InlineData("CHOOSE(1,5,1/0)", "N:5")] [InlineData("CHOOSE(2,A1,B1)", "T:a")]
    [InlineData("ROW(7)", "E:#VALUE!")] [InlineData("ROW()", "N:100")] [InlineData("COLUMN()", "N:26")] [InlineData("COLUMN(B5:A3)", "N:1")] [InlineData("ROW(B5:A3)", "N:3")]
    [InlineData("ROWS(5)", "N:1")] [InlineData("COLUMNS(\"x\")", "N:1")] [InlineData("ROWS(A1)", "N:1")]
    public void Lookups(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f, Fixture));

    [Fact]
    public void Row_Omitted_OwnCell()
    {
        var wb = new Workbook();
        wb.SetCell("C9", "=ROW()+COLUMN()");
        Assert.Equal("N:12", wb.GetValue("C9").ToCanonicalString());
    }

    [Fact]
    public void Approximate_TextKeys()
    {
        var f = new (string, string)[] { ("A1", "apple"), ("A2", "banana"), ("A3", "cherry"), ("B1", "1"), ("B2", "2"), ("B3", "3") };
        Assert.Equal("N:2", CoreTestHost.Eval("=VLOOKUP(\"BANANAS\",A1:B3,2)", f));
        Assert.Equal("E:#N/A", CoreTestHost.Eval("=VLOOKUP(\"aaa\",A1:B3,2)", f));
        Assert.Equal("N:3", CoreTestHost.Eval("=MATCH(\"zzz\",A1:A3)", f));
    }

    [Fact]
    public void ExactMatch_BooleansAndErrorKeys()
    {
        var f = new (string, string)[] { ("A1", "=1/0"), ("A2", "TRUE"), ("A3", "FALSE"), ("B2", "yes"), ("B3", "no") };
        Assert.Equal("T:no", CoreTestHost.Eval("=VLOOKUP(FALSE,A1:B3,2,FALSE)", f));
        Assert.Equal("T:yes", CoreTestHost.Eval("=VLOOKUP(TRUE,A1:B3,2,FALSE)", f));
        Assert.Equal("E:#N/A", CoreTestHost.Eval("=MATCH(1,A1:A3,0)", f));
    }

    [Fact]
    public void Approximate_On100kRows()
    {
        var wb = new Workbook();
        var edits = new List<KeyValuePair<string, string>>();
        for (int r = 1; r <= 100_000; r++) edits.Add(KeyValuePair.Create("A" + r, (r * 2).ToString()));
        wb.ApplyBatch(edits);
        wb.SetCell("C1", "=MATCH(150001,A1:A100000)");
        wb.SetCell("C2", "=VLOOKUP(99999,A1:A100000,1)");
        Assert.Equal("N:75000", wb.GetValue("C1").ToCanonicalString());
        Assert.Equal("N:99998", wb.GetValue("C2").ToCanonicalString());
    }

    private static readonly (string, string)[] Padded =
    [
        ("A1", "1"), ("A2", "3"), ("A3", "5"), ("A4", "7"), ("B1", "a"), ("B2", "b"), ("B3", "c"), ("B4", "d"),
    ];

    [Theory]
    [InlineData("VLOOKUP(4,A1:B10,2)", "T:b")] [InlineData("MATCH(1E+307,A1:A10)", "N:4")] [InlineData("VLOOKUP(\"x\",A1:B4,2)", "E:#N/A")]
    [InlineData("MATCH(TRUE,A1:A4)", "E:#N/A")] [InlineData("MATCH(\"x\",A1:A10)", "E:#N/A")] [InlineData("MATCH(0,A1:A10)", "E:#N/A")]
    public void Approximate_SkipsOtherKindsAndBlanks(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f, Padded));

    [Fact]
    public void Approximate_MixedKindsSortedPerKind()
    {
        var f = new (string, string)[] { ("A1", "1"), ("A2", "5"), ("A3", "9"), ("A4", "apple"), ("A5", "pear"), ("A6", "zebra"), ("A7", "FALSE"), ("A8", "TRUE") };
        Assert.Equal("N:2", CoreTestHost.Eval("=MATCH(6,A1:A8)", f));
        Assert.Equal("N:3", CoreTestHost.Eval("=MATCH(100,A1:A8)", f));
        Assert.Equal("N:5", CoreTestHost.Eval("=MATCH(\"q\",A1:A8)", f));
        Assert.Equal("N:6", CoreTestHost.Eval("=MATCH(\"zzz\",A1:A8)", f));
        Assert.Equal("E:#N/A", CoreTestHost.Eval("=MATCH(\"a\",A1:A8)", f));
        Assert.Equal("N:7", CoreTestHost.Eval("=MATCH(FALSE,A1:A8)", f));
        Assert.Equal("N:8", CoreTestHost.Eval("=MATCH(TRUE,A1:A8)", f));
    }

    [Fact]
    public void Approximate_DescendingWithTrailingBlanks()
    {
        var f = new (string, string)[] { ("A1", "9"), ("A2", "5"), ("A3", "1") };
        Assert.Equal("N:2", CoreTestHost.Eval("=MATCH(4,A1:A10,-1)", f));
        Assert.Equal("N:3", CoreTestHost.Eval("=MATCH(1,A1:A10,-1)", f));
        Assert.Equal("E:#N/A", CoreTestHost.Eval("=MATCH(10,A1:A10,-1)", f));
    }

    [Fact]
    public void Approximate_100kRowsWithBlankTail()
    {
        var wb = new Workbook();
        var edits = new List<KeyValuePair<string, string>>();
        for (int r = 1; r <= 50_000; r++) edits.Add(KeyValuePair.Create("A" + r, (r * 2).ToString()));
        wb.ApplyBatch(edits);
        wb.SetCell("C1", "=MATCH(70001,A1:A100000)");
        Assert.Equal("N:35000", wb.GetValue("C1").ToCanonicalString());
    }
}
