namespace Gridline.Core.Addressing;

/// <summary>A 1-based cell position. Absolute markers ($) are not stored here.</summary>
public readonly record struct CellAddress(int Row, int Col)
{
    public const int MaxRows = 100_000;
    public const int MaxColumns = 702;

    /// <summary>Multiplier packing the row above the column in <see cref="Key"/> (column fits in 10 bits).</summary>
    internal const int KeyStride = 1024;

    /// <summary>Row-major sort key.</summary>
    public int Key => Row * KeyStride + Col;

    internal static int KeyOf(int row, int col) => row * KeyStride + col;

    public static CellAddress FromKey(int key) => new(key / KeyStride, key % KeyStride);

    /// <summary>Accepts [$]COL[$]ROW, case-insensitive, within grid bounds.</summary>
    public static bool TryParse(string? s, out CellAddress a)
    {
        a = default;
        return s is not null && TryParse(s.AsSpan(), out a);
    }

    internal static bool TryParse(ReadOnlySpan<char> s, out CellAddress a)
    {
        a = default;
        int i = 0;
        if (i < s.Length && s[i] == '$') i++;
        int colStart = i;
        while (i < s.Length && (s[i] is >= 'A' and <= 'Z' or >= 'a' and <= 'z')) i++;
        if (!ColumnName.TryParse(s[colStart..i], out int col) || col > MaxColumns) return false;
        if (i < s.Length && s[i] == '$') i++;
        int rowStart = i;
        long row = 0;
        while (i < s.Length && s[i] is >= '0' and <= '9')
        {
            row = row * 10 + (s[i] - '0');
            if (row > MaxRows) return false;
            i++;
        }
        if (i == rowStart || i != s.Length || row < 1) return false;
        a = new CellAddress((int)row, col);
        return true;
    }

    public static CellAddress Parse(string s) =>
        TryParse(s, out var a) ? a : throw new ArgumentException($"Invalid cell address: '{s}'", nameof(s));

    public override string ToString() => ColumnName.ToLetters(Col) + Row.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
