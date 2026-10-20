using Gridline.Core.Addressing;

namespace Gridline.Core;

/// <summary>
/// Engine-wide cell identity (CR1.2, §6.1): <c>((long)sheetId &lt;&lt; 32) | (uint)cellKey</c>. Sheet ids and cell
/// keys are non-negative, so global keys are non-negative and order by (sheet id, row-major cell key).
/// </summary>
internal static class GlobalKey
{
    public static long Of(int sheetId, int cellKey) => ((long)sheetId << 32) | (uint)cellKey;

    public static int Sheet(long g) => (int)(g >> 32);

    public static int Cell(long g) => (int)(g & 0xFFFF_FFFFL);

    public static CellAddress Address(long g) => CellAddress.FromKey(Cell(g));
}
