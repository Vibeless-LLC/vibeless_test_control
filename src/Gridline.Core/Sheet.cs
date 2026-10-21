namespace Gridline.Core;

/// <summary>
/// One sheet of a workbook (CR1.1): a stable internal <see cref="Id"/> (never reused within one workbook), its
/// current <see cref="Name"/> and its own cell store. Position is the index in the workbook's ordered list.
/// </summary>
internal sealed class Sheet(int id, string name)
{
    public int Id { get; } = id;
    public string Name { get; set; } = name;
    public CellStore Store { get; } = new();
}

/// <summary>One entry of a sheet layout (the ordered sheet list) as recorded in undo steps (CR1.1, §9.3).</summary>
internal readonly record struct SheetEntry(int Id, string Name);
