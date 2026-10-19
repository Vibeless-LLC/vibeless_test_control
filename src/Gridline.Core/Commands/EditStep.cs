namespace Gridline.Core.Commands;

/// <summary>One cell's input change: <paramref name="Key"/> went from <paramref name="OldInput"/> to <paramref name="NewInput"/> (§9.3).</summary>
internal sealed record CellEdit(int Key, string OldInput, string NewInput);

/// <summary>
/// One undo step (§9.3): the input diffs of every changed cell (inputs only, never values) and, when the step changed
/// defined names, the full name lists before and after as (name in defined casing, canonical <c>refersTo</c>).
/// Null names mean "names unchanged". Keys in <paramref name="Cells"/> are distinct.
/// </summary>
internal sealed record EditStep(
    long Id,
    IReadOnlyList<CellEdit> Cells,
    IReadOnlyList<KeyValuePair<string, string>>? NamesBefore,
    IReadOnlyList<KeyValuePair<string, string>>? NamesAfter);
