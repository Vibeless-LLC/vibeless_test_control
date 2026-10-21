namespace Gridline.Core;

/// <summary>One sheet of a workbook snapshot (CR1.5): its name and its cells as (unqualified address, input) pairs.</summary>
public sealed record SheetSnapshot(string Name, IReadOnlyList<KeyValuePair<string, string>> Cells);
