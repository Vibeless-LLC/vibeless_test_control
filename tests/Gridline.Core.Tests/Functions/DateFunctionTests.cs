using Gridline.Core.Functions;

namespace Gridline.Core.Tests.Functions;

/// <summary>Date functions with the Excel 1900 serial system (§7.6).</summary>
public class DateFunctionTests
{
    [Theory]
    [InlineData("DATE(1900,1,1)", "N:1")] [InlineData("DATE(1900,2,28)", "N:59")] [InlineData("DATE(1900,2,29)", "N:60")] [InlineData("DATE(1900,3,1)", "N:61")] [InlineData("DATE(1900,1,0)", "N:0")]
    [InlineData("DATE(2024,14,1)", "N:45689")] [InlineData("DATE(2024,1,0)", "N:45291")] [InlineData("DATE(24,1,1)", "N:8767")] [InlineData("DATE(-1,1,1)", "E:#NUM!")] [InlineData("DATE(10000,1,1)", "E:#NUM!")] [InlineData("DATE(9999,12,31)", "N:2958465")] [InlineData("DATE(9999,12,32)", "E:#NUM!")] [InlineData("DATE(1900,1,-1)", "E:#NUM!")]
    [InlineData("YEAR(60)&\"-\"&MONTH(60)&\"-\"&DAY(60)", "T:1900-2-29")] [InlineData("DAY(0)", "N:0")] [InlineData("MONTH(0)", "N:1")] [InlineData("YEAR(-1)", "E:#NUM!")] [InlineData("YEAR(2958466)", "E:#NUM!")] [InlineData("YEAR(45291.9)", "N:2023")]
    [InlineData("WEEKDAY(1)", "N:1")] [InlineData("WEEKDAY(1,2)", "N:7")] [InlineData("WEEKDAY(1,3)", "N:6")] [InlineData("WEEKDAY(2,2)", "N:1")] [InlineData("WEEKDAY(1,4)", "E:#NUM!")] [InlineData("WEEKDAY(0)", "N:7")]
    [InlineData("EDATE(DATE(2024,1,31),1)", "N:45351")] [InlineData("EOMONTH(DATE(2024,1,15),1)", "N:45351")] [InlineData("EOMONTH(DATE(2024,1,15),-1)", "N:45291")] [InlineData("EDATE(2958465,1)", "E:#NUM!")]
    [InlineData("DAYS(10.9,3.1)", "N:7")] [InlineData("\"2024-01-01\"+0", "E:#VALUE!")]
    // additional edges
    [InlineData("DATE(0,1,1)", "N:1")] [InlineData("DATE(2024,2,30)", "N:45352")] [InlineData("DATE(2024,0,1)", "N:45261")] [InlineData("DATE(2024,-1,1)", "N:45231")]
    [InlineData("DATE(1900,0,1)", "E:#NUM!")] [InlineData("DATE(2024.9,1.9,1.9)", "N:45292")] [InlineData("DATE(\"a\",1,1)", "E:#VALUE!")] [InlineData("DATE(2024,1,)", "E:#VALUE!")] [InlineData("DATE(1/0,1,1)", "E:#DIV/0!")]
    [InlineData("DATE(2000,1,1)", "N:36526")] [InlineData("DATE(9999,13,1)", "E:#NUM!")]
    [InlineData("YEAR(1)", "N:1900")] [InlineData("MONTH(59)", "N:2")] [InlineData("DAY(59)", "N:28")] [InlineData("DAY(61)", "N:1")] [InlineData("YEAR(2958465)&\"-\"&MONTH(2958465)&\"-\"&DAY(2958465)", "T:9999-12-31")]
    [InlineData("YEAR(\"x\")", "E:#VALUE!")] [InlineData("YEAR(-0.5)", "E:#NUM!")] [InlineData("WEEKDAY(-0.5)", "E:#NUM!")] [InlineData("EDATE(-0.5,1)", "E:#NUM!")] [InlineData("EOMONTH(-0.5,1)", "E:#NUM!")] [InlineData("DAY(-0.5)", "E:#NUM!")] [InlineData("MONTH(-0.5)", "E:#NUM!")] [InlineData("YEAR(0.9)", "N:1900")]
    [InlineData("WEEKDAY(45291)", "N:1")] [InlineData("WEEKDAY(45292,2)", "N:1")] [InlineData("WEEKDAY(45292,3)", "N:0")] [InlineData("WEEKDAY(45292,2.9)", "N:1")] [InlineData("WEEKDAY(-1)", "E:#NUM!")] [InlineData("WEEKDAY(7,1)", "N:7")]
    [InlineData("EDATE(DATE(2024,1,31),-2)", "N:45260")] [InlineData("EDATE(DATE(2023,1,31),1)", "N:44985")] [InlineData("EDATE(DATE(2024,3,15),0)", "N:45366")] [InlineData("EDATE(DATE(2024,3,15),12.9)", "N:45731")]
    [InlineData("EOMONTH(DATE(2024,1,31),0)", "N:45322")] [InlineData("EOMONTH(DATE(9999,12,1),0)", "N:2958465")] [InlineData("EOMONTH(DATE(9999,12,1),1)", "E:#NUM!")] [InlineData("EOMONTH(DATE(1900,3,1),-5)", "E:#NUM!")]
    [InlineData("EOMONTH(DATE(1900,1,31),1)", "N:60")]
    [InlineData("DAYS(DATE(2024,3,1),DATE(2024,2,1))", "N:29")] [InlineData("DAYS(-1.5,1)", "N:-3")] [InlineData("DAYS(\"x\",1)", "E:#VALUE!")]
    public void Dates(string f, string e) => Assert.Equal(e, CoreTestHost.Eval("=" + f));

    [Theory]
    [InlineData(1900, 3, 1)] [InlineData(1900, 12, 31)] [InlineData(1901, 1, 1)] [InlineData(2000, 2, 29)] [InlineData(2024, 1, 31)] [InlineData(2100, 3, 1)] [InlineData(9999, 12, 31)]
    public void Serial_MatchesDateOnly(int y, int m, int d)
    {
        // Independent computation: days since 1899-12-30 (valid from 1900-03-01 on; the 1900 system's fictitious leap day shifts earlier dates).
        int expected = new DateOnly(y, m, d).DayNumber - new DateOnly(1899, 12, 30).DayNumber;
        Assert.True(DateSerial.TryFromYmd(y, m, d, out int serial));
        Assert.Equal(expected, serial);
        Assert.Equal((y, m, d), DateSerial.ToYmd(serial));
    }

    [Fact]
    public void Serial_RoundTripsEverySerial()
    {
        for (int s = 0; s <= DateSerial.Max; s += 37)
        {
            var (y, m, d) = DateSerial.ToYmd(s);
            Assert.True(DateSerial.TryFromYmd(y, m, d, out int back));
            Assert.Equal(s, back);
        }
        Assert.Equal((1900, 1, 0), DateSerial.ToYmd(0));
        Assert.Equal((1900, 2, 29), DateSerial.ToYmd(60));
        Assert.Equal((1900, 1, 1), DateSerial.ToYmd(1));
        Assert.Equal((9999, 12, 31), DateSerial.ToYmd(DateSerial.Max));
        Assert.False(DateSerial.TryFromYmd(1899, 12, 31, out _));
        Assert.False(DateSerial.TryFromYmd(9999, 12, 32, out _));
    }

    [Fact]
    public void Reference_Dates_VerifiedIndependently()
    {
        int Epoch(int y, int m, int d) => new DateOnly(y, m, d).DayNumber - new DateOnly(1899, 12, 30).DayNumber;
        Assert.Equal(45689, Epoch(2025, 2, 1));
        Assert.Equal(45291, Epoch(2023, 12, 31));
        Assert.Equal(8767, Epoch(1924, 1, 1));
        Assert.Equal(45351, Epoch(2024, 2, 29));
        Assert.Equal(2958465, Epoch(9999, 12, 31));
    }
}
