using Gridline.Core.Addressing;

namespace Gridline.Core;

/// <summary>Sheet list, active sheet and qualified addresses (CR1.1–CR1.3).</summary>
public sealed partial class Workbook
{
    private const string DefaultSheetName = "Sheet1";

    private readonly List<Sheet> _sheets = [];
    private readonly Dictionary<int, Sheet> _sheetsById = [];
    private readonly Dictionary<string, Sheet> _sheetsByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sheets removed by the layout being committed; readable until <see cref="DropRemovedSheets"/>.</summary>
    private readonly Dictionary<int, Sheet> _removing = [];

    /// <summary>Next fresh sheet id; ids are never reused within one workbook (redo reuses the id recorded in its step).</summary>
    private int _nextSheetId;
    private int _activeId;

    /// <summary>Raised after the sheet list, order, names or the active sheet changed, including through undo/redo (CR1.1).</summary>
    public event EventHandler? SheetsChanged;

    /// <summary>Sheet names in workbook order, in their defined casing (CR1.3).</summary>
    public IReadOnlyList<string> SheetNames
    {
        get
        {
            var names = new string[_sheets.Count];
            for (int i = 0; i < names.Length; i++) names[i] = _sheets[i].Name;
            return names;
        }
    }

    /// <summary>
    /// The active sheet (CR1.1): unqualified API addresses refer to it. Setting it (case-insensitively) is not
    /// undoable, records no step and does not dirty the workbook; an unknown name throws <see cref="ArgumentException"/>.
    /// </summary>
    public string ActiveSheet
    {
        get => _sheetsById[_activeId].Name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_sheetsByName.TryGetValue(value, out var s)) throw new ArgumentException($"Unknown sheet: '{value}'", nameof(value));
            if (s.Id == _activeId) return;
            _activeId = s.Id;
            SheetsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Adds an empty sheet at <paramref name="index"/> (appended when null) as one undo step (CR1.3). The active
    /// sheet is unchanged. Throws <see cref="ArgumentException"/> for an invalid or duplicate (case-insensitive)
    /// name, <see cref="ArgumentOutOfRangeException"/> for an index outside <c>0..Count</c>, and
    /// <see cref="InvalidOperationException"/> when the workbook already has <see cref="Addressing.SheetNames.MaxSheets"/> sheets.
    /// </summary>
    public void AddSheet(string name, int? index = null)
    {
        if (!Addressing.SheetNames.IsValid(name)) throw new ArgumentException($"Invalid sheet name: '{name}'", nameof(name));
        if (_sheetsByName.ContainsKey(name)) throw new ArgumentException($"Duplicate sheet name: '{name}'", nameof(name));
        int at = index ?? _sheets.Count;
        if (at < 0 || at > _sheets.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Must be between 0 and {_sheets.Count}.");
        if (_sheets.Count >= Addressing.SheetNames.MaxSheets)
            throw new InvalidOperationException($"A workbook has at most {Addressing.SheetNames.MaxSheets} sheets.");

        var layout = new List<SheetEntry>(Layout());
        layout.Insert(at, new SheetEntry(_nextSheetId++, name));
        Commit([], newNames: null, newSheets: layout, record: true);
    }

    /// <summary>
    /// Moves a sheet (case-insensitive name) to <paramref name="newIndex"/> in <c>0..Count-1</c> as one undo step;
    /// moving to its current index records nothing (CR1.3). Unknown name → <see cref="ArgumentException"/>.
    /// </summary>
    public void MoveSheet(string name, int newIndex)
    {
        var sheet = SheetByName(name, nameof(name));
        if (newIndex < 0 || newIndex >= _sheets.Count)
            throw new ArgumentOutOfRangeException(nameof(newIndex), newIndex, $"Must be between 0 and {_sheets.Count - 1}.");
        var layout = new List<SheetEntry>(Layout());
        int from = _sheets.IndexOf(sheet);
        if (from == newIndex) return;
        var entry = layout[from];
        layout.RemoveAt(from);
        layout.Insert(newIndex, entry);
        Commit([], newNames: null, newSheets: layout, record: true);
    }

    /// <summary>
    /// Addresses of all cells with input (CR1.3): with one sheet, unqualified and row-major (as in v1); with more,
    /// qualified (<c>Sheet1!A1</c>, <c>'My Data'!B2</c>), in sheet order and then row-major.
    /// </summary>
    public IReadOnlyList<string> GetUsedAddresses()
    {
        int total = 0;
        foreach (var s in _sheets) total += s.Store.Count;
        var result = new List<string>(total);
        foreach (var s in _sheets)
            foreach (int key in s.Store.KeysRowMajor()) result.Add(FormatAddress(s, key));
        return result;
    }

    /// <summary>Unqualified addresses of the cells with input on one sheet (case-insensitive name), row-major.</summary>
    public IReadOnlyList<string> GetUsedAddresses(string sheet)
    {
        var s = SheetByName(sheet, nameof(sheet));
        var result = new List<string>(s.Store.Count);
        foreach (int key in s.Store.KeysRowMajor()) result.Add(CellAddress.FromKey(key).ToString());
        return result;
    }

    private Sheet Active => _sheetsById[_activeId];

    private void InstallInitialSheet(string name)
    {
        var s = new Sheet(_nextSheetId++, name);
        _sheets.Add(s);
        _sheetsById[s.Id] = s;
        _sheetsByName[s.Name] = s;
        _activeId = s.Id;
    }

    /// <summary>Sheet id for a sheet name as written in a formula (case-insensitive), or null if there is no such sheet (CR1.2).</summary>
    private int? ResolveSheet(string name) => _sheetsByName.TryGetValue(name, out var s) ? s.Id : null;

    /// <summary>Defined casing of an (upper-cased) sheet name, or null when no such sheet exists (§5.5, CR1.2).</summary>
    private string? SheetCasing(string upper) => _sheetsByName.TryGetValue(upper, out var s) ? s.Name : null;

    /// <summary>A sheet that exists, or is being removed by the current commit.</summary>
    private Sheet SheetById(int id) =>
        _sheetsById.TryGetValue(id, out var s) || _removing.TryGetValue(id, out s)
            ? s
            : throw new InvalidOperationException($"No sheet with id {id}.");

    /// <summary>The store of a global key's sheet, or null when that sheet does not exist.</summary>
    private CellStore? StoreOf(long g)
    {
        int id = GlobalKey.Sheet(g);
        return _sheetsById.TryGetValue(id, out var s) || _removing.TryGetValue(id, out s) ? s.Store : null;
    }

    private Sheet SheetByName(string name, string paramName)
    {
        ArgumentNullException.ThrowIfNull(name, paramName);
        return _sheetsByName.TryGetValue(name, out var s) ? s : throw new ArgumentException($"Unknown sheet: '{name}'", paramName);
    }

    /// <summary>The named sheet (case-insensitive), or the active sheet when <paramref name="name"/> is null (CR1.1).</summary>
    private Sheet SheetOrActive(string? name, string paramName) => name is null ? Active : SheetByName(name, paramName);

    /// <summary>
    /// Parses a qualified or unqualified cell address (CR1.3) to a global key. Unqualified means the active sheet.
    /// An invalid address or unknown sheet throws <see cref="ArgumentException"/>.
    /// </summary>
    private long ParseCell(string address, string paramName)
    {
        if (!QualifiedAddress.TryParseCell(address, out string? sheet, out var a))
            throw new ArgumentException($"Invalid cell address: '{address}'", paramName);
        return GlobalKey.Of(SheetOrActive(sheet, paramName).Id, a.Key);
    }

    /// <summary>A cell's address for output: unqualified with one sheet, else <c>Format(name) + "!" + A1</c> (CR1.3).</summary>
    private string FormatAddress(Sheet sheet, int cellKey)
    {
        string a = CellAddress.FromKey(cellKey).ToString();
        return _sheets.Count == 1 ? a : Addressing.SheetNames.Format(sheet.Name) + "!" + a;
    }

    /// <summary>The current layout (ordered sheet ids and names).</summary>
    private SheetEntry[] Layout()
    {
        var layout = new SheetEntry[_sheets.Count];
        for (int i = 0; i < layout.Length; i++) layout[i] = new SheetEntry(_sheets[i].Id, _sheets[i].Name);
        return layout;
    }

    /// <summary>
    /// Commit step 1 (CR1.1): installs <paramref name="layout"/>, creating sheets for new ids and keeping removed
    /// ones in <see cref="_removing"/>. If the active sheet was removed, the sheet now at its old index becomes
    /// active, else the last one. Returns the upper-cased sheet names whose existence or id changed.
    /// </summary>
    private List<string> InstallLayout(IReadOnlyList<SheetEntry> layout)
    {
        var oldIds = new Dictionary<string, int>(_sheetsByName.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (n, s) in _sheetsByName) oldIds[n] = s.Id;
        int oldActiveIndex = _sheets.FindIndex(s => s.Id == _activeId);

        var old = new Dictionary<int, Sheet>(_sheetsById);
        _sheets.Clear();
        _sheetsById.Clear();
        _sheetsByName.Clear();
        foreach (var e in layout)
        {
            if (old.Remove(e.Id, out var s)) s.Name = e.Name;
            else
            {
                s = new Sheet(e.Id, e.Name);
                _nextSheetId = Math.Max(_nextSheetId, e.Id + 1);
            }
            _sheets.Add(s);
            _sheetsById[s.Id] = s;
            _sheetsByName[s.Name] = s;
        }
        foreach (var (id, s) in old) _removing[id] = s;

        if (!_sheetsById.ContainsKey(_activeId))
            _activeId = _sheets[oldActiveIndex < _sheets.Count ? oldActiveIndex : _sheets.Count - 1].Id;

        var changed = new List<string>();
        foreach (var (n, id) in oldIds)
            if (!_sheetsByName.TryGetValue(n, out var s) || s.Id != id) changed.Add(n.ToUpperInvariant());
        foreach (var (n, _) in _sheetsByName)
            if (!oldIds.ContainsKey(n)) changed.Add(n.ToUpperInvariant());
        return changed;
    }

    /// <summary>Commit step 4: formulas left on removed sheets leave the dependency graph; their stores are dropped.</summary>
    private void DropRemovedSheets()
    {
        foreach (var (id, s) in _removing)
            foreach (var (key, cell) in s.Store.All)
                if (cell.Kind == CellKind.Formula) _graph.SetDependencies(GlobalKey.Of(id, key), null, _resolveSheet);
        _removing.Clear();
    }
}
