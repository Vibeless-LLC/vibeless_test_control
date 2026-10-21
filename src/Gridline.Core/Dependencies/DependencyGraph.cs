using Gridline.Core.Addressing;

namespace Gridline.Core.Dependencies;

/// <summary>A cell reference in a formula: <paramref name="Sheet"/> as written (null = unqualified) and its cell key (CR1.2).</summary>
internal readonly record struct SheetCell(string? Sheet, int Key);

/// <summary>A normalized range reference in a formula: <paramref name="Sheet"/> as written (null = unqualified) (CR1.2).</summary>
internal readonly record struct SheetRange(string? Sheet, RangeAddress Range);

/// <summary>Static dependencies of one formula (§6.1, CR1.2). Sheet names are as written; names are upper-cased.</summary>
internal sealed record FormulaDeps(IReadOnlyList<SheetCell> Cells, IReadOnlyList<SheetRange> Ranges, IReadOnlyList<string> Names);

/// <summary>
/// Reverse dependency index (§6.1, §6.7, CR1.2) over global keys (<see cref="GlobalKey"/>): for a precedent cell,
/// which formula cells must be recalculated. Direct cell references are a key → dependent-set map. Ranges are
/// deduplicated per (sheet, normalized range), each holding its dependent set, and indexed per sheet and column: a
/// short range is registered in each 256-row block it overlaps, a tall range in the column's "tall" list. A lookup
/// scans only the cell's block and its column's tall list on the cell's sheet. A defined name is a virtual node with
/// id <c>-(index+1)</c>; its target is registered in the range index with the name node as dependent, so a cell
/// inside the target yields the formulas that depend on the name.
/// <para>
/// Sheet references are resolved when a formula is registered: unqualified refs use the formula's own sheet, and a
/// qualified ref goes through the caller's resolver (null = no such sheet, so nothing is registered for it). The
/// resolved ids are stored with the formula so unregistering never re-resolves. Every qualified sheet name, resolved
/// or not, is indexed (case-insensitively) to its formulas, so <see cref="Rebind"/> can re-resolve them when a sheet
/// name starts or stops existing.
/// </para>
/// </summary>
internal sealed class DependencyGraph
{
    internal const int BlockRows = 256;
    internal const int MaxShortBlocks = 8;
    private const int MaxCol = 1023; // CellAddress.Key packs Col into 10 bits.
    private const int Unresolved = -1;

    private sealed class RangeEntry(RangeAddress range)
    {
        public readonly RangeAddress Range = range;
        public readonly HashSet<long> Dependents = [];
    }

    private sealed class ColumnIndex
    {
        public List<RangeEntry>?[]? Blocks;
        public readonly List<RangeEntry> Tall = [];
    }

    private sealed class NameNode(string name, long id)
    {
        public readonly string Name = name;
        public readonly long Id = id;
        public (int Sheet, RangeAddress Range)? Target;
        public readonly HashSet<long> Dependents = [];
    }

    /// <summary>A formula's deps plus the sheet id each qualified ref resolved to (cells then ranges; -1 = unresolved).</summary>
    private readonly record struct Registration(FormulaDeps Deps, int[]? Resolved);

    private readonly Dictionary<long, Registration> _deps = [];
    private readonly Dictionary<long, HashSet<long>> _cellDependents = [];
    private readonly Dictionary<(int Sheet, RangeAddress Range), RangeEntry> _ranges = [];
    private readonly Dictionary<int, ColumnIndex?[]> _columns = [];
    private readonly Dictionary<string, NameNode> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NameNode> _nameNodes = [];
    private readonly Dictionary<string, HashSet<long>> _sheetRefs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of distinct (sheet, range) entries currently indexed (diagnostics/tests).</summary>
    internal int DistinctRangeCount => _ranges.Count;

    /// <summary>
    /// Replaces <paramref name="formulaKey"/>'s dependencies; null removes them. Qualified sheet names are passed to
    /// <paramref name="resolveSheet"/> as written and must be matched case-insensitively by it.
    /// </summary>
    public void SetDependencies(long formulaKey, FormulaDeps? deps, Func<string, int?> resolveSheet)
    {
        if (deps is not null && _deps.TryGetValue(formulaKey, out var prev))
        {
            var next = new Registration(deps, Resolve(deps, resolveSheet));
            if (SameTargets(prev, next))
            {
                // Only the spelling of sheet names changed (a sheet rename, CR1.9 P-8): the cell, range and name
                // registrations stay; the sheet-name index and the stored deps follow the new spelling.
                ReindexSheetNames(formulaKey, prev.Deps, next.Deps);
                _deps[formulaKey] = next;
                return;
            }
        }
        if (_deps.Remove(formulaKey, out var old)) Apply(formulaKey, old, add: false);
        if (deps is null) return;
        var reg = new Registration(deps, Resolve(deps, resolveSheet));
        _deps[formulaKey] = reg;
        Apply(formulaKey, reg, add: true);
    }

    /// <summary>
    /// True when two registrations register exactly the same graph entries: the same cell keys, ranges and names in
    /// the same order, the same refs qualified, and each qualified ref resolved to the same sheet id. Sheet-name
    /// spelling may differ. Formulas with many qualified refs answer false (the full path is used for them).
    /// </summary>
    private static bool SameTargets(Registration a, Registration b)
    {
        const int MaxQualified = 32;
        FormulaDeps x = a.Deps, y = b.Deps;
        if (x.Cells.Count != y.Cells.Count || x.Ranges.Count != y.Ranges.Count || x.Names.Count != y.Names.Count) return false;
        if (a.Resolved is null || b.Resolved is null || a.Resolved.Length > MaxQualified) return false;
        if (!a.Resolved.AsSpan().SequenceEqual(b.Resolved)) return false;
        for (int i = 0; i < x.Cells.Count; i++)
            if (x.Cells[i].Key != y.Cells[i].Key || (x.Cells[i].Sheet is null) != (y.Cells[i].Sheet is null)) return false;
        for (int i = 0; i < x.Ranges.Count; i++)
            if (x.Ranges[i].Range != y.Ranges[i].Range || (x.Ranges[i].Sheet is null) != (y.Ranges[i].Sheet is null)) return false;
        for (int i = 0; i < x.Names.Count; i++)
            if (!string.Equals(x.Names[i], y.Names[i], StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>Moves <paramref name="f"/> in the sheet-name index from the names only <paramref name="from"/> qualifies with to those only <paramref name="to"/> does.</summary>
    private void ReindexSheetNames(long f, FormulaDeps from, FormulaDeps to)
    {
        foreach (string n in QualifiedSheets(from))
            if (!Qualifies(to, n)) SheetRef(n, f, add: false);
        foreach (string n in QualifiedSheets(to))
            if (!Qualifies(from, n)) SheetRef(n, f, add: true);
    }

    private static IEnumerable<string> QualifiedSheets(FormulaDeps d)
    {
        foreach (var c in d.Cells) if (c.Sheet is { } s) yield return s;
        foreach (var r in d.Ranges) if (r.Sheet is { } s) yield return s;
    }

    private static bool Qualifies(FormulaDeps d, string sheet)
    {
        foreach (var c in d.Cells) if (c.Sheet is { } s && s.Equals(sheet, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var r in d.Ranges) if (r.Sheet is { } s && s.Equals(sheet, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Sets (or clears, with a null sheet or target) the target of a defined name on sheet <paramref name="sheetId"/>.
    /// Its dependents are kept either way.
    /// </summary>
    public void SetNameTarget(string upperName, int? sheetId, RangeAddress? target)
    {
        var node = GetOrAddName(upperName);
        (int Sheet, RangeAddress Range)? norm = sheetId is { } s && target is { } t ? (s, Normalize(t)) : null;
        if (node.Target == norm) return;
        if (node.Target is { } oldT) RemoveRangeDependent(oldT.Sheet, oldT.Range, node.Id);
        node.Target = norm;
        if (norm is { } newT) AddRangeDependent(newT.Sheet, newT.Range, node.Id);
    }

    /// <summary>
    /// Formulas whose text qualifies a reference with <paramref name="upperSheetName"/> (case-insensitive), whether
    /// or not that sheet exists. The graph must not be modified while enumerating.
    /// </summary>
    public IEnumerable<long> FormulasReferencingSheet(string upperSheetName) =>
        _sheetRefs.TryGetValue(upperSheetName, out var set) ? set : [];

    /// <summary>
    /// Re-resolves and re-registers every formula in <see cref="FormulasReferencingSheet"/> for the name (after that
    /// name started or stopped existing, or changed id) and returns them in ascending key order. Formulas in
    /// <paramref name="alreadyRegistered"/> were registered against the current sheets already and are only returned.
    /// </summary>
    public IReadOnlyList<long> Rebind(string upperSheetName, Func<string, int?> resolveSheet, IReadOnlySet<long>? alreadyRegistered = null)
    {
        if (!_sheetRefs.TryGetValue(upperSheetName, out var set)) return [];
        var formulas = new long[set.Count];
        set.CopyTo(formulas);
        Array.Sort(formulas);
        foreach (long f in formulas)
        {
            if (alreadyRegistered is not null && alreadyRegistered.Contains(f)) continue;
            var old = _deps[f];
            Apply(f, old, add: false);
            var reg = new Registration(old.Deps, Resolve(old.Deps, resolveSheet));
            _deps[f] = reg;
            Apply(f, reg, add: true);
        }
        return formulas;
    }

    /// <summary>
    /// Formula cells that reference <paramref name="key"/> directly, through a range, or through a name whose
    /// target contains it. A dependent may be yielded more than once when it reaches the cell several ways.
    /// The graph must not be modified while enumerating.
    /// </summary>
    public IEnumerable<long> DirectDependents(long key)
    {
        if (_cellDependents.TryGetValue(key, out var direct))
            foreach (long d in direct) yield return d;

        foreach (var e in EntriesContaining(key))
            foreach (long d in e.Dependents)
                if (d >= 0) yield return d;
                else foreach (long x in _nameNodes[(int)(-d - 1)].Dependents) yield return x;
    }

    /// <summary>
    /// Distinct formula cells that depend directly on any of <paramref name="keys"/>. Each distinct range entry
    /// and name node is expanded at most once, so a bulk change of many cells under shared ranges costs
    /// O(keys scanned + entries hit + distinct dependents), not O(keys × fan-in).
    /// </summary>
    public IEnumerable<long> DistinctDirectDependentsOfMany(IEnumerable<long> keys)
    {
        var seen = new HashSet<long>();
        var entries = new HashSet<RangeEntry>(ReferenceEqualityComparer.Instance);
        var names = new HashSet<long>();
        foreach (long key in keys)
        {
            if (_cellDependents.TryGetValue(key, out var direct))
                foreach (long d in direct)
                    if (seen.Add(d)) yield return d;
            foreach (var e in EntriesContaining(key))
            {
                if (!entries.Add(e)) continue;
                foreach (long d in e.Dependents)
                {
                    if (d >= 0) { if (seen.Add(d)) yield return d; }
                    else if (names.Add(d))
                        foreach (long x in _nameNodes[(int)(-d - 1)].Dependents)
                            if (seen.Add(x)) yield return x;
                }
            }
        }
    }

    private IEnumerable<RangeEntry> EntriesContaining(long key)
    {
        if (!_columns.TryGetValue(GlobalKey.Sheet(key), out var columns)) yield break;
        var addr = GlobalKey.Address(key);
        if ((uint)addr.Col > MaxCol || columns[addr.Col] is not { } col) yield break;
        if (col.Blocks is { } blocks)
        {
            int b = (addr.Row - 1) / BlockRows;
            if ((uint)b < (uint)blocks.Length && blocks[b] is { } list)
                foreach (var e in list)
                    if (e.Range.Contains(addr)) yield return e;
        }
        foreach (var e in col.Tall)
            if (e.Range.Contains(addr)) yield return e;
    }

    /// <summary>Formula cells that reference the name (whether or not it is defined).</summary>
    public IEnumerable<long> NameDependents(string upperName) =>
        _names.TryGetValue(upperName, out var node) ? node.Dependents : [];

    /// <summary>Resolves each qualified ref (cells then ranges) to a sheet id, or -1; null when nothing is qualified.</summary>
    private static int[]? Resolve(FormulaDeps deps, Func<string, int?> resolveSheet)
    {
        int n = 0;
        foreach (var c in deps.Cells) if (c.Sheet is not null) n++;
        foreach (var r in deps.Ranges) if (r.Sheet is not null) n++;
        if (n == 0) return null;
        var resolved = new int[n];
        int i = 0;
        foreach (var c in deps.Cells) if (c.Sheet is { } s) resolved[i++] = resolveSheet(s) ?? Unresolved;
        foreach (var r in deps.Ranges) if (r.Sheet is { } s) resolved[i++] = resolveSheet(s) ?? Unresolved;
        return resolved;
    }

    /// <summary>Adds or removes every registration of <paramref name="f"/> exactly as resolved in <paramref name="reg"/>.</summary>
    private void Apply(long f, Registration reg, bool add)
    {
        int own = GlobalKey.Sheet(f);
        int qi = 0;
        foreach (var c in reg.Deps.Cells)
        {
            int sheet = own;
            if (c.Sheet is { } name)
            {
                sheet = reg.Resolved![qi++];
                SheetRef(name, f, add);
            }
            if (sheet == Unresolved) continue;
            long k = GlobalKey.Of(sheet, c.Key);
            if (add)
            {
                if (!_cellDependents.TryGetValue(k, out var set)) _cellDependents[k] = set = [];
                set.Add(f);
            }
            else if (_cellDependents.TryGetValue(k, out var set) && set.Remove(f) && set.Count == 0)
                _cellDependents.Remove(k);
        }
        foreach (var r in reg.Deps.Ranges)
        {
            int sheet = own;
            if (r.Sheet is { } name)
            {
                sheet = reg.Resolved![qi++];
                SheetRef(name, f, add);
            }
            if (sheet == Unresolved) continue;
            if (add) AddRangeDependent(sheet, Normalize(r.Range), f);
            else RemoveRangeDependent(sheet, Normalize(r.Range), f);
        }
        foreach (var n in reg.Deps.Names)
        {
            if (add) GetOrAddName(n).Dependents.Add(f);
            else if (_names.TryGetValue(n, out var node)) node.Dependents.Remove(f);
        }
    }

    /// <summary>Adds/removes <paramref name="f"/> in the sheet-reference index entry of <paramref name="name"/> (upper-cased key).</summary>
    private void SheetRef(string name, long f, bool add)
    {
        if (add)
        {
            if (!_sheetRefs.TryGetValue(name, out var set)) _sheetRefs[name.ToUpperInvariant()] = set = [];
            set.Add(f);
        }
        else if (_sheetRefs.TryGetValue(name, out var set) && set.Remove(f) && set.Count == 0)
            _sheetRefs.Remove(name);
    }

    private NameNode GetOrAddName(string name)
    {
        if (!_names.TryGetValue(name, out var node))
        {
            node = new NameNode(name.ToUpperInvariant(), -(_nameNodes.Count + 1L));
            _nameNodes.Add(node);
            _names[node.Name] = node;
        }
        return node;
    }

    private static RangeAddress Normalize(RangeAddress r) => new(
        new CellAddress(Math.Min(r.TopLeft.Row, r.BottomRight.Row), Math.Min(r.TopLeft.Col, r.BottomRight.Col)),
        new CellAddress(Math.Max(r.TopLeft.Row, r.BottomRight.Row), Math.Max(r.TopLeft.Col, r.BottomRight.Col)));

    private void AddRangeDependent(int sheet, RangeAddress r, long dependent)
    {
        if (!_ranges.TryGetValue((sheet, r), out var entry))
        {
            entry = new RangeEntry(r);
            _ranges[(sheet, r)] = entry;
            Index(sheet, entry, add: true);
        }
        entry.Dependents.Add(dependent);
    }

    private void RemoveRangeDependent(int sheet, RangeAddress r, long dependent)
    {
        if (!_ranges.TryGetValue((sheet, r), out var entry) || !entry.Dependents.Remove(dependent) || entry.Dependents.Count > 0) return;
        _ranges.Remove((sheet, r));
        Index(sheet, entry, add: false);
    }

    /// <summary>Adds/removes a range entry in its sheet's per-column block/tall index.</summary>
    private void Index(int sheet, RangeEntry entry, bool add)
    {
        if (!_columns.TryGetValue(sheet, out var columns))
        {
            if (!add) return;
            _columns[sheet] = columns = new ColumnIndex?[MaxCol + 1];
        }
        var r = entry.Range;
        int c0 = Math.Max(r.TopLeft.Col, 0), c1 = Math.Min(r.BottomRight.Col, MaxCol);
        int b0 = Math.Max(r.TopLeft.Row - 1, 0) / BlockRows, b1 = Math.Max(r.BottomRight.Row - 1, 0) / BlockRows;
        bool tall = b1 - b0 + 1 > MaxShortBlocks;
        for (int c = c0; c <= c1; c++)
        {
            var col = columns[c];
            if (col is null)
            {
                if (!add) continue;
                columns[c] = col = new ColumnIndex();
            }
            if (tall)
            {
                if (add) col.Tall.Add(entry); else col.Tall.Remove(entry);
                continue;
            }
            if (col.Blocks is null || col.Blocks.Length <= b1)
            {
                if (!add) { if (col.Blocks is null) continue; }
                else Array.Resize(ref col.Blocks, Math.Max(b1 + 1, (col.Blocks?.Length ?? 0) * 2));
            }
            for (int b = b0; b <= b1 && b < col.Blocks!.Length; b++)
            {
                if (add) (col.Blocks[b] ??= []).Add(entry);
                else col.Blocks[b]?.Remove(entry);
            }
        }
    }
}
