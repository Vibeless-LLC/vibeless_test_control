using Gridline.Core.Addressing;

namespace Gridline.Core;

/// <summary>
/// Sparse store of non-blank cells keyed by <see cref="CellAddress.Key"/>. A dictionary gives O(1) lookup; each
/// column also keeps a sorted list of its occupied rows (binary-search insert), so a range is enumerated
/// row-major by merging the column lists, touching only occupied cells.
/// </summary>
internal sealed class CellStore
{
    /// <summary>Ranges with at most this many cells are enumerated by probing the dictionary directly.</summary>
    private const int ProbeArea = 64;

    private readonly Dictionary<int, Cell> _cells = [];
    private readonly List<int>?[] _rowsByCol = new List<int>?[CellAddress.MaxColumns + 1];

    public int Count => _cells.Count;

    public bool TryGet(int key, out Cell c) => _cells.TryGetValue(key, out c!);

    public void Set(int key, Cell c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (_cells.TryAdd(key, c))
        {
            var a = CellAddress.FromKey(key);
            var rows = _rowsByCol[a.Col] ??= [];
            // Appending in row order (the common load case) is O(1).
            if (rows.Count == 0 || rows[^1] < a.Row) rows.Add(a.Row);
            else rows.Insert(~rows.BinarySearch(a.Row), a.Row);
        }
        else _cells[key] = c;
    }

    public void Remove(int key)
    {
        if (!_cells.Remove(key)) return;
        var a = CellAddress.FromKey(key);
        var rows = _rowsByCol[a.Col]!;
        rows.RemoveAt(rows.BinarySearch(a.Row));
    }

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
        int col = r.TopLeft.Col;
        if (_rowsByCol[col] is not { } rows) yield break;
        for (int i = LowerBound(rows, r.TopLeft.Row); i < rows.Count && rows[i] <= r.BottomRight.Row; i++)
            yield return rows[i] * 1024 + col;
    }

    private IEnumerable<int> Merge(RangeAddress r)
    {
        int top = r.TopLeft.Row, bottom = r.BottomRight.Row;
        // Min-heap of (column → cursor) ordered by the cursor's cell key, which is row-major.
        var heap = new PriorityQueue<(int Col, int Index), int>();
        for (int col = r.TopLeft.Col; col <= r.BottomRight.Col; col++)
        {
            if (_rowsByCol[col] is not { Count: > 0 } rows) continue;
            int i = LowerBound(rows, top);
            if (i < rows.Count && rows[i] <= bottom) heap.Enqueue((col, i), rows[i] * 1024 + col);
        }
        while (heap.TryDequeue(out var cur, out int key))
        {
            yield return key;
            var rows = _rowsByCol[cur.Col]!;
            int next = cur.Index + 1;
            if (next < rows.Count && rows[next] <= bottom) heap.Enqueue((cur.Col, next), rows[next] * 1024 + cur.Col);
        }
    }

    private static int LowerBound(List<int> rows, int row)
    {
        int i = rows.BinarySearch(row);
        return i >= 0 ? i : ~i;
    }
}
