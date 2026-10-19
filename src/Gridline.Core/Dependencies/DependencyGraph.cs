using Gridline.Core.Addressing;

namespace Gridline.Core.Dependencies;

/// <summary>Static dependencies of one formula (§6.1). Names are upper-cased.</summary>
internal sealed record FormulaDeps(IReadOnlyList<int> Cells, IReadOnlyList<RangeAddress> Ranges, IReadOnlyList<string> Names);

/// <summary>
/// Reverse dependency index (§6.1, §6.7): for a precedent cell, which formula cells must be recalculated.
/// Direct cell references are a key → dependent-set map. Ranges are deduplicated (one entry per distinct
/// normalized range, holding its dependent set) and indexed per column: a short range is registered in each
/// 256-row block it overlaps, a tall range in the column's "tall" list. A lookup scans only the cell's
/// block and its column's tall list. A defined name is a virtual node with id <c>-(index+1)</c>; its target
/// is registered in the range index with the name node as dependent, so a cell inside the target yields
/// the formulas that depend on the name.
/// </summary>
internal sealed class DependencyGraph
{
    internal const int BlockRows = 256;
    internal const int MaxShortBlocks = 8;
    private const int MaxCol = 1023; // CellAddress.Key packs Col into 10 bits.

    private sealed class RangeEntry(RangeAddress range)
    {
        public readonly RangeAddress Range = range;
        public readonly HashSet<int> Dependents = [];
    }

    private sealed class ColumnIndex
    {
        public List<RangeEntry>?[]? Blocks;
        public readonly List<RangeEntry> Tall = [];
    }

    private sealed class NameNode(string name, int id)
    {
        public readonly string Name = name;
        public readonly int Id = id;
        public RangeAddress? Target;
        public readonly HashSet<int> Dependents = [];
    }

    private readonly Dictionary<int, FormulaDeps> _deps = [];
    private readonly Dictionary<int, HashSet<int>> _cellDependents = [];
    private readonly Dictionary<RangeAddress, RangeEntry> _ranges = [];
    private readonly ColumnIndex?[] _columns = new ColumnIndex?[MaxCol + 1];
    private readonly Dictionary<string, NameNode> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NameNode> _nameNodes = [];

    /// <summary>Number of distinct ranges currently indexed (diagnostics/tests).</summary>
    internal int DistinctRangeCount => _ranges.Count;

    /// <summary>Replaces <paramref name="cellKey"/>'s dependencies; null removes them.</summary>
    public void SetDependencies(int cellKey, FormulaDeps? deps)
    {
        if (_deps.Remove(cellKey, out var old)) Unregister(cellKey, old);
        if (deps is null) return;
        _deps[cellKey] = deps;
        foreach (int c in deps.Cells)
        {
            if (!_cellDependents.TryGetValue(c, out var set)) _cellDependents[c] = set = [];
            set.Add(cellKey);
        }
        foreach (var r in deps.Ranges) AddRangeDependent(Normalize(r), cellKey);
        foreach (var n in deps.Names) GetOrAddName(n).Dependents.Add(cellKey);
    }

    /// <summary>Sets (or clears, with null) the target of a defined name. Its dependents are kept either way.</summary>
    public void SetNameTarget(string upperName, RangeAddress? target)
    {
        var node = GetOrAddName(upperName);
        RangeAddress? norm = target is { } t ? Normalize(t) : null;
        if (node.Target == norm) return;
        if (node.Target is { } oldT) RemoveRangeDependent(oldT, node.Id);
        node.Target = norm;
        if (norm is { } newT) AddRangeDependent(newT, node.Id);
    }

    /// <summary>
    /// Formula cells that reference <paramref name="key"/> directly, through a range, or through a name whose
    /// target contains it. A dependent may be yielded more than once when it reaches the cell several ways.
    /// The graph must not be modified while enumerating.
    /// </summary>
    public IEnumerable<int> DirectDependents(int key)
    {
        if (_cellDependents.TryGetValue(key, out var direct))
            foreach (int d in direct) yield return d;

        var addr = CellAddress.FromKey(key);
        if ((uint)addr.Col > MaxCol || _columns[addr.Col] is not { } col) yield break;

        if (col.Blocks is { } blocks)
        {
            int b = (addr.Row - 1) / BlockRows;
            if ((uint)b < (uint)blocks.Length && blocks[b] is { } list)
                foreach (var e in list)
                    if (e.Range.Contains(addr))
                        foreach (int d in e.Dependents)
                            if (d >= 0) yield return d;
                            else foreach (int x in _nameNodes[-d - 1].Dependents) yield return x;
        }
        foreach (var e in col.Tall)
            if (e.Range.Contains(addr))
                foreach (int d in e.Dependents)
                    if (d >= 0) yield return d;
                    else foreach (int x in _nameNodes[-d - 1].Dependents) yield return x;
    }

    /// <summary>Formula cells that reference the name (whether or not it is defined).</summary>
    public IEnumerable<int> NameDependents(string upperName) =>
        _names.TryGetValue(upperName, out var node) ? node.Dependents : [];

    private void Unregister(int cellKey, FormulaDeps old)
    {
        foreach (int c in old.Cells)
            if (_cellDependents.TryGetValue(c, out var set) && set.Remove(cellKey) && set.Count == 0)
                _cellDependents.Remove(c);
        foreach (var r in old.Ranges) RemoveRangeDependent(Normalize(r), cellKey);
        foreach (var n in old.Names)
            if (_names.TryGetValue(n, out var node)) node.Dependents.Remove(cellKey);
    }

    private NameNode GetOrAddName(string name)
    {
        if (!_names.TryGetValue(name, out var node))
        {
            node = new NameNode(name.ToUpperInvariant(), -(_nameNodes.Count + 1));
            _nameNodes.Add(node);
            _names[node.Name] = node;
        }
        return node;
    }

    private static RangeAddress Normalize(RangeAddress r) => new(
        new CellAddress(Math.Min(r.TopLeft.Row, r.BottomRight.Row), Math.Min(r.TopLeft.Col, r.BottomRight.Col)),
        new CellAddress(Math.Max(r.TopLeft.Row, r.BottomRight.Row), Math.Max(r.TopLeft.Col, r.BottomRight.Col)));

    private void AddRangeDependent(RangeAddress r, int dependent)
    {
        if (!_ranges.TryGetValue(r, out var entry))
        {
            entry = new RangeEntry(r);
            _ranges[r] = entry;
            Index(entry, add: true);
        }
        entry.Dependents.Add(dependent);
    }

    private void RemoveRangeDependent(RangeAddress r, int dependent)
    {
        if (!_ranges.TryGetValue(r, out var entry) || !entry.Dependents.Remove(dependent) || entry.Dependents.Count > 0) return;
        _ranges.Remove(r);
        Index(entry, add: false);
    }

    private void Index(RangeEntry entry, bool add)
    {
        var r = entry.Range;
        int c0 = Math.Max(r.TopLeft.Col, 0), c1 = Math.Min(r.BottomRight.Col, MaxCol);
        int b0 = Math.Max(r.TopLeft.Row - 1, 0) / BlockRows, b1 = Math.Max(r.BottomRight.Row - 1, 0) / BlockRows;
        bool tall = b1 - b0 + 1 > MaxShortBlocks;
        for (int c = c0; c <= c1; c++)
        {
            var col = _columns[c];
            if (col is null)
            {
                if (!add) continue;
                _columns[c] = col = new ColumnIndex();
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
