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
        public readonly List<int> Rows = [];
        public readonly List<Cell> Cells = [];

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
        if (_cells.TryAdd(key, c))
        {
            // Appending in row order (the common load case) is O(1).
            if (col.Rows.Count == 0 || col.Rows[^1] < a.Row)
            {
                col.Rows.Add(a.Row);
                col.Cells.Add(c);
            }
            else
            {
                int i = ~col.Rows.BinarySearch(a.Row);
                col.Rows.Insert(i, a.Row);
                col.Cells.Insert(i, c);
            }
        }
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
                int key = row * 1024 + col;
                if (_cells.ContainsKey(key)) yield return key;
            }
    }

    private IEnumerable<int> SingleColumn(RangeAddress r)
    {
        int c = r.TopLeft.Col;
        if (_columns[c] is not { } col) yield break;
        var rows = col.Rows;
        for (int i = col.LowerBound(r.TopLeft.Row); i < rows.Count && rows[i] <= r.BottomRight.Row; i++)
            yield return rows[i] * 1024 + c;
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
            if (i < col.Rows.Count && col.Rows[i] <= bottom) heap.Enqueue((c, i), col.Rows[i] * 1024 + c);
        }
        while (heap.TryDequeue(out var cur, out int key))
        {
            yield return key;
            var rows = _columns[cur.Col]!.Rows;
            int next = cur.Index + 1;
            if (next < rows.Count && rows[next] <= bottom) heap.Enqueue((cur.Col, next), rows[next] * 1024 + cur.Col);
        }
    }
}
