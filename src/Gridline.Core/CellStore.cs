using Gridline.Core.Addressing;

namespace Gridline.Core;

/// <summary>
/// Sparse store of non-blank cells keyed by <see cref="CellAddress.Key"/>. A dictionary gives O(1) lookup; each
/// column also keeps its occupied rows in a sorted list (binary-search insert) with the cells in a parallel list,
/// so a range is enumerated row-major by merging the column lists, touching only occupied cells, and a
/// single-column range is read straight from the column's lists without dictionary lookups (§12 P-4, P-7).
/// </summary>
internal sealed class CellStore
{
    /// <summary>Ranges with at most this many cells are enumerated by probing the dictionary directly.</summary>
    private const int ProbeArea = 64;

    /// <summary>Occupied rows of one column, ascending, and their cells at the same indices.</summary>
    internal sealed class Column
    {
        public List<int> Rows = [];
        public List<Cell> Cells = [];

        /// <summary>Index of the first row ≥ <paramref name="row"/>.</summary>
        public int LowerBound(int row)
        {
            int i = Rows.BinarySearch(row);
            return i >= 0 ? i : ~i;
        }
    }

    private readonly Dictionary<int, Cell> _cells = [];
    private readonly Column?[] _columns = new Column?[CellAddress.MaxColumns + 1];

    public int Count => _cells.Count;

    public bool TryGet(int key, out Cell c) => _cells.TryGetValue(key, out c!);

    public void Set(int key, Cell c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var a = CellAddress.FromKey(key);
        var col = _columns[a.Col] ??= new Column();
        if (_cells.TryAdd(key, c)) ColumnInsert(col, a.Row, c);
        else
        {
            _cells[key] = c;
            col.Cells[col.Rows.BinarySearch(a.Row)] = c;
        }
    }

    public void Remove(int key)
    {
        if (!_cells.Remove(key)) return;
        var a = CellAddress.FromKey(key);
        var col = _columns[a.Col]!;
        int i = col.Rows.BinarySearch(a.Row);
        col.Rows.RemoveAt(i);
        col.Cells.RemoveAt(i);
    }

    private static void ColumnInsert(Column col, int row, Cell c)
    {
        // Appending in row order (the common load case) is O(1).
        if (col.Rows.Count == 0 || col.Rows[^1] < row)
        {
            col.Rows.Add(row);
            col.Cells.Add(c);
        }
        else
        {
            int i = ~col.Rows.BinarySearch(row);
            col.Rows.Insert(i, row);
            col.Cells.Insert(i, c);
        }
    }

    /// <summary>Columns receiving more writes than this in one <see cref="SetMany"/> are rebuilt by a merge.</summary>
    private const int MergeThreshold = 16;

    /// <summary>
    /// Applies many writes (null cell = remove) with distinct keys. A column receiving many writes is rebuilt by one
    /// linear merge of its sorted lists with the sorted writes, so bulk moves (insert/delete of rows) cost
    /// O(column size + writes log writes) per column instead of one list shift per write.
    /// </summary>
    public void SetMany(IReadOnlyList<(int Key, Cell? Cell)> writes)
    {
        if (writes.Count <= MergeThreshold)
        {
            foreach (var (key, cell) in writes)
                if (cell is null) Remove(key); else Set(key, cell);
            return;
        }
        var byCol = new Dictionary<int, List<(int Row, Cell? Cell)>>();
        foreach (var (key, cell) in writes)
        {
            var a = CellAddress.FromKey(key);
            if (cell is null) { if (!_cells.Remove(key)) continue; }
            else _cells[key] = cell;
            if (!byCol.TryGetValue(a.Col, out var list)) byCol[a.Col] = list = [];
            list.Add((a.Row, cell));
        }
        foreach (var (c, list) in byCol)
        {
            var col = _columns[c] ??= new Column();
            if (list.Count <= MergeThreshold)
            {
                foreach (var (row, cell) in list)
                {
                    int i = col.Rows.BinarySearch(row);
                    if (cell is null) { col.Rows.RemoveAt(i); col.Cells.RemoveAt(i); }
                    else if (i >= 0) col.Cells[i] = cell;
                    else ColumnInsert(col, row, cell);
                }
                continue;
            }
            list.Sort(static (x, y) => x.Row.CompareTo(y.Row));
            var oldRows = col.Rows;
            var oldCells = col.Cells;
            var rows = new List<int>(oldRows.Count + list.Count);
            var cells = new List<Cell>(oldRows.Count + list.Count);
            int p = 0, q = 0;
            while (p < oldRows.Count || q < list.Count)
            {
                if (q == list.Count || (p < oldRows.Count && oldRows[p] < list[q].Row))
                {
                    rows.Add(oldRows[p]); cells.Add(oldCells[p]); p++;
                    continue;
                }
                if (p < oldRows.Count && oldRows[p] == list[q].Row) p++; // replaced or removed
                if (list[q].Cell is { } nc) { rows.Add(list[q].Row); cells.Add(nc); }
                q++;
            }
            col.Rows = rows;
            col.Cells = cells;
        }
    }

    /// <summary>All stored cells (unordered). Must not be enumerated while the store is modified.</summary>
    public IReadOnlyDictionary<int, Cell> All => _cells;

    /// <summary>The highest occupied row of column <paramref name="col"/>, or 0 if it has none.</summary>
    public int LastRow(int col) => GetColumn(col) is { Rows.Count: > 0 } c ? c.Rows[^1] : 0;

    /// <summary>The occupied rows and cells of column <paramref name="col"/>, or null if it has none.</summary>
    public Column? GetColumn(int col) => (uint)col < (uint)_columns.Length ? _columns[col] : null;

    /// <summary>All occupied keys in row-major order (sorts on demand).</summary>
    public IEnumerable<int> KeysRowMajor()
    {
        var keys = new int[_cells.Count];
        _cells.Keys.CopyTo(keys, 0);
        Array.Sort(keys);
        return keys;
    }

    /// <summary>Occupied keys inside <paramref name="r"/> (normalized), row-major.</summary>
    public IEnumerable<int> KeysInRange(RangeAddress r)
    {
        long area = (long)r.Rows * r.Cols;
        if (area <= ProbeArea) return Probe(r);
        if (r.Cols == 1) return SingleColumn(r);
        return Merge(r);
    }

    private IEnumerable<int> Probe(RangeAddress r)
    {
        for (int row = r.TopLeft.Row; row <= r.BottomRight.Row; row++)
            for (int col = r.TopLeft.Col; col <= r.BottomRight.Col; col++)
            {
                int key = CellAddress.KeyOf(row, col);
                if (_cells.ContainsKey(key)) yield return key;
            }
    }

    private IEnumerable<int> SingleColumn(RangeAddress r)
    {
        int c = r.TopLeft.Col;
        if (_columns[c] is not { } col) yield break;
        var rows = col.Rows;
        for (int i = col.LowerBound(r.TopLeft.Row); i < rows.Count && rows[i] <= r.BottomRight.Row; i++)
            yield return CellAddress.KeyOf(rows[i], c);
    }

    private IEnumerable<int> Merge(RangeAddress r)
    {
        int top = r.TopLeft.Row, bottom = r.BottomRight.Row;
        // Min-heap of (column → cursor) ordered by the cursor's cell key, which is row-major.
        var heap = new PriorityQueue<(int Col, int Index), int>();
        for (int c = r.TopLeft.Col; c <= r.BottomRight.Col; c++)
        {
            if (_columns[c] is not { Rows.Count: > 0 } col) continue;
            int i = col.LowerBound(top);
            if (i < col.Rows.Count && col.Rows[i] <= bottom) heap.Enqueue((c, i), CellAddress.KeyOf(col.Rows[i], c));
        }
        while (heap.TryDequeue(out var cur, out int key))
        {
            yield return key;
            var rows = _columns[cur.Col]!.Rows;
            int next = cur.Index + 1;
            if (next < rows.Count && rows[next] <= bottom) heap.Enqueue((cur.Col, next), CellAddress.KeyOf(rows[next], cur.Col));
        }
    }
}
