using System.Globalization;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.IO.Tests;

public class CsvRoundTripProperties
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz ,;\"\r\n\t";
    private static readonly Gen<string> Suffix =
        Gen.Choose(0, 12).SelectMany(n => Gen.ArrayOf(Gen.Elements(Alphabet.ToCharArray()), n)).Select(a => new string(a));

    private static readonly Gen<string> Cell = Gen.OneOf(
        Gen.Constant(""),
        Gen.Elements(0.0, 1.5, -2.25, 1e300, 1e-300, 123456789.125, double.Epsilon, -0.1).Select(d => d.ToString("R", CultureInfo.InvariantCulture)),
        Gen.Elements("TRUE", "FALSE"),
        Suffix.Select(s => "x" + s),
        Suffix.Select(s => "'=" + s));

    private static readonly Gen<string[][]> Grid =
        Gen.Choose(0, 10).SelectMany(rows => Gen.Choose(1, 10).SelectMany(cols =>
            Gen.ArrayOf(Gen.ArrayOf(Cell, cols), rows)));

    private static bool RoundTrips(string[][] grid, char delim)
    {
        var wb = new Workbook();
        for (int r = 0; r < grid.Length; r++)
            for (int c = 0; c < grid[r].Length; c++)
                if (grid[r][c].Length > 0) wb.SetCell(((char)('A' + c)).ToString() + (r + 1), grid[r][c]);
        var back = CsvFile.Import(CsvFile.Export(wb, delim), delim);
        for (int r = 0; r < grid.Length; r++)
            for (int c = 0; c < grid[r].Length; c++)
            {
                var a = ((char)('A' + c)).ToString() + (r + 1);
                if (wb.GetValue(a) != back.GetValue(a)) return false;
            }
        return true;
    }

    [Property(MaxTest = 200)]
    public Property Export_Import_PreservesValues() =>
        Prop.ForAll(Grid.ToArbitrary(), g => RoundTrips(g, ',') && RoundTrips(g, ';'));
}
