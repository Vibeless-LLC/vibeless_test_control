using System.Text;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.IO.Tests;

/// <summary>File format version 2 (CR1.5, §8.1).</summary>
public class GridlineFileV2Tests
{
    private const string Canonical = """
        {
          "format": "gridline",
          "version": 2,
          "activeSheet": "Sheet1",
          "names": {
            "Rate": "Data!$B$1"
          },
          "sheets": [
            {
              "name": "Sheet1",
              "cells": [
                ["A1", "=Data!B2*Rate"]
              ]
            },
            {
              "name": "Data",
              "cells": []
            }
          ]
        }

        """;

    private static string Lf(string s) => s.ReplaceLineEndings("\n");

    [Fact]
    public void V2_SerializeMatchesCanonicalExample()
    {
        var wb = new Workbook();
        wb.AddSheet("Data");
        wb.DefineName("Rate", "Data!$B$1");
        wb.SetCell("A1", "=Data!B2*Rate");
        Assert.Equal(Encoding.UTF8.GetBytes(Lf(Canonical)), GridlineFile.SerializeToUtf8(wb));
    }

    [Fact]
    public void V2_CanonicalExample_LoadsAndResavesIdentically()
    {
        var bytes = Encoding.UTF8.GetBytes(Lf(Canonical));
        var wb = GridlineFile.Load(bytes);
        Assert.Equal(new[] { "Sheet1", "Data" }, wb.SheetNames);
        Assert.Equal(bytes, GridlineFile.SerializeToUtf8(wb));
    }

    [Fact]
    public void V1_LoadMigratesNames()
    {
        var wb = GridlineFile.Load("{\"format\":\"gridline\",\"version\":1,\"names\":{\"Rate\":\"$B$1\"},\"cells\":[[\"A1\",\"=Rate\"],[\"B1\",\"7\"]]}"u8);
        Assert.Equal(new[] { "Sheet1" }, wb.SheetNames);
        Assert.Equal("Sheet1", wb.ActiveSheet);
        Assert.Equal("Sheet1!$B$1", wb.GetNames()[0].Value);
        Assert.Equal("=Rate", wb.GetInput("A1"));
        Assert.Equal("N:7", wb.GetValue("A1").ToCanonicalString());
        Assert.Contains("\"version\": 2,", GridlineFile.Serialize(wb));
        Assert.False(wb.IsDirty);
    }

    [Theory]
    [InlineData("$B$1")]
    [InlineData("Sheet1!$B$1")]
    [InlineData("sheet1!$B$1")]
    public void V1_LoadMigratesNames_UnqualifiedOrAlreadyQualified(string refersTo)
    {
        var wb = GridlineFile.Load(System.Text.Encoding.UTF8.GetBytes(
            "{\"format\":\"gridline\",\"version\":1,\"names\":{\"Rate\":\"" + refersTo + "\"},\"cells\":[[\"A1\",\"=Rate\"],[\"B1\",\"7\"]]}"));
        Assert.Equal("Sheet1!$B$1", wb.GetNames()[0].Value);
        Assert.Equal("N:7", wb.GetValue("A1").ToCanonicalString());
    }

    [Fact]
    public void V1_RefNameStaysRef()
    {
        var wb = GridlineFile.Load("{\"format\":\"gridline\",\"version\":1,\"names\":{\"Gone\":\"#REF!\"},\"cells\":[]}"u8);
        Assert.Equal("#REF!", wb.GetNames()[0].Value);
    }

    [Fact]
    public void V3_Unsupported() =>
        Assert.Contains("unsupported version", Assert.Throws<GridlineFormatException>(() =>
            GridlineFile.Load("{\"format\":\"gridline\",\"version\":3,\"sheets\":[]}"u8)).Message);

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonPositiveVersion_Invalid(string v) =>
        Assert.Contains("invalid version", Assert.Throws<GridlineFormatException>(() =>
            GridlineFile.Load(Encoding.UTF8.GetBytes("{\"format\":\"gridline\",\"version\":" + v + ",\"cells\":[]}"))).Message);

    [Theory]
    [InlineData("\"activeSheet\":\"A\",\"sheets\":[{\"name\":\"A\",\"cells\":[]},{\"name\":\"a\",\"cells\":[]}]")]
    [InlineData("\"activeSheet\":\"a*b\",\"sheets\":[{\"name\":\"a*b\",\"cells\":[]}]")]
    [InlineData("\"activeSheet\":\"A\",\"sheets\":[]")]
    [InlineData("\"activeSheet\":\"B\",\"sheets\":[{\"name\":\"A\",\"cells\":[]}]")]
    [InlineData("\"sheets\":[{\"name\":\"A\",\"cells\":[]}]")]
    [InlineData("\"activeSheet\":1,\"sheets\":[{\"name\":\"A\",\"cells\":[]}]")]
    [InlineData("\"activeSheet\":\"A\"")]
    [InlineData("\"activeSheet\":\"A\",\"sheets\":{}")]
    [InlineData("\"activeSheet\":\"A\",\"sheets\":[1]")]
    [InlineData("\"activeSheet\":\"A\",\"sheets\":[{\"cells\":[]}]")]
    [InlineData("\"activeSheet\":\"A\",\"sheets\":[{\"name\":\"A\"}]")]
    [InlineData("\"activeSheet\":\"A\",\"sheets\":[{\"name\":\"A\",\"cells\":{}}]")]
    [InlineData("\"activeSheet\":\"A\",\"sheets\":[{\"name\":\"A\",\"cells\":[[\"A1\",\"1\"],[\"a1\",\"2\"]]}]")]
    [InlineData("\"activeSheet\":\"A\",\"names\":{\"R\":\"Nope!$A$1\"},\"sheets\":[{\"name\":\"A\",\"cells\":[]}]")]
    public void V2_Invalid_Throws(string body) =>
        Assert.Throws<GridlineFormatException>(() =>
            GridlineFile.Load(Encoding.UTF8.GetBytes("{\"format\":\"gridline\",\"version\":2," + body + "}")));

    [Fact]
    public void V2_RoundTripQuotedNames()
    {
        var wb = new Workbook();
        wb.AddSheet("My Sheet");
        wb.AddSheet("It's");
        wb.AddSheet("2024");
        wb.SetCell("A1", "=SUM('My Sheet'!A1:A2)+'It''s'!B1+'2024'!C3");
        wb.SetCell("'My Sheet'!A1", "4");
        wb.SetCell("'My Sheet'!A2", "='It''s'!B1");
        wb.SetCell("'It''s'!B1", "10");
        wb.SetCell("'2024'!C3", "=Sheet1!B1");
        wb.DefineName("Pick", "'My Sheet'!$A$1:$A$2");
        wb.ActiveSheet = "It's";

        var bytes = GridlineFile.SerializeToUtf8(wb);
        var back = GridlineFile.Load(bytes);
        Assert.Equal(bytes, GridlineFile.SerializeToUtf8(back));
        Assert.Equal(wb.SheetNames, back.SheetNames);
        foreach (var a in wb.GetUsedAddresses())
        {
            Assert.Equal(wb.GetInput(a), back.GetInput(a));
            Assert.Equal(wb.GetValue(a).ToCanonicalString(), back.GetValue(a).ToCanonicalString());
        }
        Assert.Contains("\"name\": \"It's\"", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void V2_ActiveSheetPreserved()
    {
        var wb = new Workbook();
        wb.AddSheet("Second");
        wb.AddSheet("Third");
        wb.ActiveSheet = "Second";
        var back = GridlineFile.Load(GridlineFile.SerializeToUtf8(wb));
        Assert.Equal("Second", back.ActiveSheet);
        Assert.Contains("\"activeSheet\": \"Second\"", GridlineFile.Serialize(back));
    }

    [Fact]
    public void V2_ActiveSheetMatchedCaseInsensitively_SavedInDefinedCasing()
    {
        var wb = GridlineFile.Load("{\"format\":\"gridline\",\"version\":2,\"activeSheet\":\"data\",\"sheets\":[{\"name\":\"Sheet1\",\"cells\":[]},{\"name\":\"Data\",\"cells\":[]}]}"u8);
        Assert.Equal("Data", wb.ActiveSheet);
    }

    [Fact]
    public void V2_LoadedWorkbookIsClean()
    {
        var wb = GridlineFile.Load(Encoding.UTF8.GetBytes(Lf(Canonical)));
        Assert.False(wb.IsDirty);
        Assert.False(wb.CanUndo);
    }

    [Fact]
    public void FromSnapshot_MultiSheet_Rejects()
    {
        var one = new[] { new SheetSnapshot("A", []) };
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot(Array.Empty<SheetSnapshot>(), [], "A"));
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot(one, [], "B"));
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot([new("A", []), new("a", [])], [], "A"));
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot([new("a:b", [])], [], "a:b"));
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot(one, [new("R", "$A$1"), new("r", "$A$2")], "A"));
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot([new("A", [new("A1", "1"), new("a1", "2")])], [], "A"));
        Assert.Throws<ArgumentException>(() => Workbook.FromSnapshot([new("A", [new("B!A1", "1")])], [], "A"));
    }

    [Fact]
    public void FromSnapshot_MultiSheet_ResolvesForwardAndUnqualifiedNames()
    {
        var wb = Workbook.FromSnapshot(
            [new("A", [new("A1", "=B!A1+R")]), new("B", [new("A1", "5"), new("C1", "2")])],
            [new("R", "$C$1")], "B");
        Assert.Equal("B", wb.ActiveSheet);
        Assert.Equal("B!$C$1", wb.GetNames()[0].Value);
        Assert.Equal("N:7", wb.GetValue("A!A1").ToCanonicalString());
        Assert.False(wb.IsDirty);
    }
}
