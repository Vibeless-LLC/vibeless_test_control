using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Gridline.Core;
using Gridline.IO;

namespace Gridline.IO.Tests;

public class RoundTripProperties
{
    private static readonly Gen<char> AnyChar = Gen.OneOf(
        Gen.Choose(0, 0x7F).Select(i => (char)i),
        Gen.Choose(0x80, 0xFFFF).Select(i => (char)i));

    private static readonly Gen<string> Input =
        Gen.Choose(0, 200).SelectMany(n => Gen.ArrayOf(AnyChar, n)).Select(a => new string(a));

    private static readonly Gen<string> Address =
        Gen.Choose(1, 702).SelectMany(c => Gen.Choose(1, 100000).Select(r => Col(c) + r));

    private static string Col(int c) => c <= 26 ? ((char)('A' + c - 1)).ToString() : ((char)('A' + (c - 1) / 26 - 1)).ToString() + (char)('A' + (c - 1) % 26);

    [Property(MaxTest = 200)]
    public Property Save_Load_Save_IsIdentity() =>
        Prop.ForAll(Gen.Choose(0, 50).SelectMany(n => Gen.ArrayOf(Address.SelectMany(a => Input.Select(i => (a, i))), n)).ToArbitrary(), pairs =>
        {
            var wb = new Workbook();
            foreach (var (a, i) in pairs) wb.SetCell(a, i);
            var bytes = GridlineFile.SerializeToUtf8(wb);
            var again = GridlineFile.SerializeToUtf8(GridlineFile.Load(bytes));
            return bytes.AsSpan().SequenceEqual(again);
        });

    private static readonly string[] SheetNamePool = ["Sheet1", "Data", "My Data", "It's", "2024", "A1B", "x.y", "TRUE", "Ünï", "q_1"];

    private sealed record MultiSheetCase(string[] Names, int Active, (int Sheet, string Address, string Text)[] Cells);

    private static readonly Gen<(int Sheet, string Address, string Text)> MultiCell =
        from si in Gen.Choose(0, 3)
        from ti in Gen.Choose(0, 3)
        from isF in Gen.Choose(0, 1)
        from a in Address
        from i in Input
        select (si, a, isF == 1 ? "=" + ti + "!B2+1" : i); // "=<index>!B2+1" is rewritten to a sheet name below

    /// <summary>1-4 distinct valid sheet names, a random active one, random cells and cross-sheet formulas.</summary>
    [Property(MaxTest = 200)]
    public Property MultiSheet_Save_Load_Save_IsIdentity() =>
        Prop.ForAll(
            (from n in Gen.Choose(1, 4)
             from names in Gen.Shuffle(SheetNamePool).Select(a => a.Take(n).ToArray())
             from active in Gen.Choose(0, n - 1)
             from k in Gen.Choose(0, 12)
             from cells in Gen.ArrayOf(MultiCell, k)
             select new MultiSheetCase(names, active, cells)).ToArbitrary(),
            c =>
            {
                var wb = new Workbook();
                wb.RenameSheet("Sheet1", c.Names[0]);
                for (int i = 1; i < c.Names.Length; i++) wb.AddSheet(c.Names[i]);
                foreach (var (si, a, text) in c.Cells)
                {
                    string input = text;
                    if (text.Length > 2 && text[0] == '=' && text[2] == '!' && char.IsDigit(text[1]))
                        input = "=" + Gridline.Core.Addressing.SheetNames.Format(c.Names[(text[1] - '0') % c.Names.Length]) + text[2..];
                    wb.SetCell(Gridline.Core.Addressing.SheetNames.Format(c.Names[si % c.Names.Length]) + "!" + a, input);
                }
                wb.ActiveSheet = c.Names[c.Active];
                var bytes = GridlineFile.SerializeToUtf8(wb);
                var back = GridlineFile.Load(bytes);
                return bytes.AsSpan().SequenceEqual(GridlineFile.SerializeToUtf8(back))
                    && back.ActiveSheet == wb.ActiveSheet
                    && wb.SheetNames.SequenceEqual(back.SheetNames);
            });
}
