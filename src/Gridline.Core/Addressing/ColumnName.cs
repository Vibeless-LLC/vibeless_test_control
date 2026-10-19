namespace Gridline.Core.Addressing;

/// <summary>Converts between 1-based column indexes (1-702) and letters (A-ZZ).</summary>
public static class ColumnName
{
    public static string ToLetters(int col)
    {
        if (col < 1 || col > CellAddress.MaxColumns)
            throw new ArgumentOutOfRangeException(nameof(col));
        return col <= 26
            ? ((char)('A' + col - 1)).ToString()
            : string.Concat((char)('A' + (col - 1) / 26 - 1), (char)('A' + (col - 1) % 26));
    }

    public static bool TryParse(ReadOnlySpan<char> letters, out int col)
    {
        col = 0;
        if (letters.Length is < 1 or > 2) return false;
        int v = 0;
        foreach (char ch in letters)
        {
            char u = ch is >= 'a' and <= 'z' ? (char)(ch - 32) : ch;
            if (u is < 'A' or > 'Z') return false;
            v = v * 26 + (u - 'A' + 1);
        }
        col = v;
        return true;
    }
}
