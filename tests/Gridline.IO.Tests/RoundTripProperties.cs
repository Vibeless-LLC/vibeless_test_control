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
}
