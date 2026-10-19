using Gridline.Core.Functions;

namespace Gridline.Core.Tests.Functions;

public class TextFunctionTests
{
    [Theory]
    [InlineData("LEN(\"abc\")", "N:3")] [InlineData("LEFT(\"abc\")", "T:a")] [InlineData("LEFT(\"abc\",9)", "T:abc")] [InlineData("RIGHT(\"abc\",-1)", "E:#VALUE!")]
    [InlineData("MID(\"abc\",4,1)", "T:")] [InlineData("MID(\"abc\",0,1)", "E:#VALUE!")] [InlineData("MID(\"abcdef\",2,3)", "T:bcd")]
    [InlineData("PROPER(\"o'neil 2nd\")", "T:O'Neil 2Nd")] [InlineData("TRIM(\"  a   b  \")", "T:a b")] [InlineData("UPPER(\"aé\")", "T:AÉ")]
    [InlineData("CONCAT(\"a\",1,TRUE)", "T:a1TRUE")] [InlineData("FIND(\"\",\"abc\",2)", "N:2")] [InlineData("FIND(\"B\",\"abc\")", "E:#VALUE!")] [InlineData("FIND(\"a\",\"abc\",5)", "E:#VALUE!")] [InlineData("FIND(\"c\",\"abc\",4)", "E:#VALUE!")] [InlineData("FIND(\"\",\"abc\",4)", "N:4")]
    [InlineData("SEARCH(\"B\",\"abc\")", "N:2")] [InlineData("SEARCH(\"a?c\",\"xabc\")", "N:2")] [InlineData("SEARCH(\"b*d\",\"abcd\")", "N:2")] [InlineData("SEARCH(\"~*\",\"a*b\")", "N:2")]
    [InlineData("SUBSTITUTE(\"aaa\",\"a\",\"b\",2)", "T:aba")] [InlineData("SUBSTITUTE(\"aaaa\",\"aa\",\"b\")", "T:bb")] [InlineData("SUBSTITUTE(\"abc\",\"\",\"x\")", "T:abc")] [InlineData("SUBSTITUTE(\"a\",\"a\",\"b\",0)", "E:#VALUE!")]
    [InlineData("REPLACE(\"abc\",2,1,\"X\")", "T:aXc")] [InlineData("REPLACE(\"abc\",9,1,\"X\")", "T:abcX")] [InlineData("REPT(\"ab\",3)", "T:ababab")] [InlineData("REPT(\"a\",-1)", "E:#VALUE!")] [InlineData("REPT(\"a\",40000)", "E:#VALUE!")]
    [InlineData("EXACT(\"a\",\"A\")", "B:FALSE")] [InlineData("VALUE(\" 1e3 \")", "N:1000")] [InlineData("VALUE(\"x\")", "E:#VALUE!")] [InlineData("LEN(1/3)", "N:17")]
    // Extras.
    [InlineData("RIGHT(\"abc\",2)", "T:bc")] [InlineData("RIGHT(\"abc\")", "T:c")] [InlineData("RIGHT(\"abc\",9)", "T:abc")] [InlineData("LEFT(\"abc\",0)", "T:")]
    [InlineData("MID(\"abc\",2,-1)", "E:#VALUE!")] [InlineData("MID(\"abc\",2,99)", "T:bc")] [InlineData("LOWER(\"ABÉ\")", "T:abé")] [InlineData("EXACT(\"a\",\"a\")", "B:TRUE")]
    [InlineData("FIND(\"b\",\"abcb\",3)", "N:4")] [InlineData("FIND(\"a\",\"abc\",0)", "E:#VALUE!")] [InlineData("SEARCH(\"B\",\"abc\",3)", "E:#VALUE!")] [InlineData("SEARCH(\"\",\"abc\",2)", "N:2")] [InlineData("SEARCH(\"~\",\"a~\")", "N:2")]
    [InlineData("SUBSTITUTE(\"abab\",\"B\",\"x\")", "T:abab")] [InlineData("SUBSTITUTE(\"aaa\",\"a\",\"b\",4)", "T:aaa")] [InlineData("SUBSTITUTE(\"aaaa\",\"aa\",\"b\",2)", "T:aab")]
    [InlineData("REPLACE(\"abc\",1,0,\"X\")", "T:Xabc")] [InlineData("REPLACE(\"abc\",2,99,\"X\")", "T:aX")] [InlineData("REPLACE(\"abc\",0,1,\"X\")", "E:#VALUE!")] [InlineData("REPLACE(\"abc\",1,-1,\"X\")", "E:#VALUE!")]
    [InlineData("REPT(\"\",40000)", "T:")] [InlineData("REPT(\"a\",0)", "T:")]
    [InlineData("LEN(1/0)", "E:#DIV/0!")] [InlineData("VALUE(5)", "N:5")] [InlineData("TRIM(NOPE)", "E:#NAME?")]
    public void Direct(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f));

    [Theory]
    [InlineData("MID(\"abc\",2,)", "E:#VALUE!")] [InlineData("MID(\"abc\",,1)", "E:#VALUE!")] [InlineData("REPLACE(\"abc\",2,,\"X\")", "E:#VALUE!")] [InlineData("REPLACE(\"abc\",,1,\"X\")", "E:#VALUE!")] [InlineData("REPLACE(\"abc\",1,1,)", "E:#VALUE!")]
    [InlineData("REPT(\"a\",)", "E:#VALUE!")] [InlineData("REPT(,2)", "E:#VALUE!")] [InlineData("FIND(,\"abc\")", "E:#VALUE!")] [InlineData("FIND(\"a\",)", "E:#VALUE!")] [InlineData("SEARCH(,\"abc\")", "E:#VALUE!")]
    [InlineData("SUBSTITUTE(\"a\",,\"b\")", "E:#VALUE!")] [InlineData("SUBSTITUTE(\"a\",\"a\",)", "E:#VALUE!")] [InlineData("SUBSTITUTE(,\"a\",\"b\")", "E:#VALUE!")]
    [InlineData("EXACT(,\"a\")", "E:#VALUE!")] [InlineData("EXACT(\"a\",)", "E:#VALUE!")] [InlineData("UPPER(,)", "E:#PARSE!")] [InlineData("LEFT(,1)", "E:#VALUE!")]
    // Optional positions keep their defaults when omitted.
    [InlineData("LEFT(\"abc\",)", "T:a")] [InlineData("RIGHT(\"abc\",)", "T:c")] [InlineData("FIND(\"b\",\"abc\",)", "N:2")] [InlineData("SEARCH(\"b\",\"abc\",)", "N:2")] [InlineData("SUBSTITUTE(\"aa\",\"a\",\"b\",)", "T:bb")]
    // Variadic: omitted is absent.
    [InlineData("CONCAT(,\"a\",)", "T:a")] [InlineData("TEXTJOIN(\",\",TRUE,,\"a\",\"b\")", "T:a,b")] [InlineData("TEXTJOIN(,TRUE,\"a\",\"b\")", "E:#VALUE!")] [InlineData("TEXTJOIN(\",\",,\"a\")", "E:#VALUE!")]
    [InlineData("VALUE()", "E:#PARSE!")] [InlineData("SEARCH(\"b*d\",\"abcd\")", "N:2")] [InlineData("SEARCH(\"?c\",\"abc\")", "N:2")] [InlineData("SEARCH(\"~**\",\"a*b\")", "N:2")]
    [InlineData("SEARCH(\"a*?c\",\"xabbc\")", "N:2")] [InlineData("SEARCH(\"a*b?d\",\"aabxd abcd\")", "N:1")] [InlineData("SEARCH(\"*c\",\"abc\")", "N:1")] [InlineData("SEARCH(\"a*\",\"bab\")", "N:2")] [InlineData("SEARCH(\"b*z\",\"abc\")", "E:#VALUE!")]
    [InlineData("SEARCH(\"a**c\",\"abc\")", "N:1")] [InlineData("SEARCH(\"c*\",\"abc\",3)", "N:3")]
    public void OmittedArgsAndSearchShapes(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f));

    [Fact] public void Search_PathologicalStarsAreFast()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal("E:#VALUE!", CoreTestHost.Eval("=SEARCH(\"a*a*a*a*b\",REPT(\"a\",32767))"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), sw.Elapsed.ToString());
        Assert.Equal(-1, Wildcard.Search("a*a*a*a*b", new string('a', 32767), 0));
    }

    [Fact] public void Concat_RangeRowMajorBlanksEmpty() => Assert.Equal("T:abc", CoreTestHost.Eval("=CONCAT(A1:B2)", ("A1","a"), ("B1","b"), ("B2","c")));
    [Fact] public void TextJoin_IgnoreEmpty() { Assert.Equal("T:a,c", CoreTestHost.Eval("=TEXTJOIN(\",\",TRUE,A1:A3)", ("A1","a"), ("A3","c"))); Assert.Equal("T:a,,c", CoreTestHost.Eval("=TEXTJOIN(\",\",FALSE,A1:A3)", ("A1","a"), ("A3","c"))); }
    [Fact] public void TextJoin_TrailingBlanksAndEmptyText() { Assert.Equal("T:a,,", CoreTestHost.Eval("=TEXTJOIN(\",\",FALSE,A1:A3)", ("A1","a"))); Assert.Equal("T:a", CoreTestHost.Eval("=TEXTJOIN(\",\",TRUE,A1:A3)", ("A1","a"), ("A2","=\"\""))); Assert.Equal("T:a,", CoreTestHost.Eval("=TEXTJOIN(\",\",FALSE,A1:A2)", ("A1","a"), ("A2","=\"\""))); }
    [Fact] public void Concat_ErrorInRangePropagates() => Assert.Equal("E:#DIV/0!", CoreTestHost.Eval("=CONCAT(A1:A2)", ("A1","a"), ("A2","=1/0")));
    [Fact] public void Concat_OverLimit() => Assert.Equal("E:#VALUE!", CoreTestHost.Eval("=CONCAT(REPT(\"a\",20000),REPT(\"a\",20000))"));
    [Fact] public void Substitute_OverLimit() => Assert.Equal("E:#VALUE!", CoreTestHost.Eval("=SUBSTITUTE(REPT(\"a\",20000),\"a\",\"bb\")"));
    [Fact] public void MaxLengthResultIsAllowed() => Assert.Equal("N:32767", CoreTestHost.Eval("=LEN(REPT(\"a\",32767))"));

    [Theory]
    [InlineData("a*c", "abc", true)] [InlineData("a*c", "ac", true)] [InlineData("a*c", "abd", false)] [InlineData("A?C", "abc", true)] [InlineData("a?c", "ac", false)]
    [InlineData("*", "", true)] [InlineData("", "", true)] [InlineData("", "a", false)] [InlineData("~*", "*", true)] [InlineData("~*", "a", false)] [InlineData("~?x", "?x", true)] [InlineData("~~", "~", true)] [InlineData("a~", "a~", true)]
    [InlineData("*a*b*", "xxaxxbxx", true)] [InlineData("*a*b*", "xxbxxaxx", false)] [InlineData("a*a*a", "aaa", true)] [InlineData("a*a*a", "aa", false)] [InlineData("é*", "ÉA", true)]
    public void Wildcard_IsMatch(string p, string t, bool e) => Assert.Equal(e, Wildcard.IsMatch(p, t));

    [Theory]
    [InlineData("b", "abc", 0, 1)] [InlineData("b", "abc", 2, -1)] [InlineData("", "abc", 3, 3)] [InlineData("", "abc", 4, -1)] [InlineData("b*d", "abcd", 0, 1)] [InlineData("c?", "abc", 0, -1)]
    public void Wildcard_Search(string p, string t, int start, int e) => Assert.Equal(e, Wildcard.Search(p, t, start));

    [Fact] public void Wildcard_PathologicalIsFastAndIterative()
    {
        string text = new string('a', 20000), pattern = string.Concat(Enumerable.Repeat("a*", 20)) + "b";
        Assert.False(Wildcard.IsMatch(pattern, text));
        Assert.True(Wildcard.IsMatch(new string('*', 50000) + "a", new string('a', 100000)));
    }
}
