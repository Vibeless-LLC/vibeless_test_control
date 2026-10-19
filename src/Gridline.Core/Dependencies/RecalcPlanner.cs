namespace Gridline.Core.Dependencies;

/// <summary>
/// Formula cells to evaluate, each once, precedents before dependents (§6.7). Cycle members are included
/// where their strongly connected component falls and are also listed in <see cref="CycleMembers"/>.
/// </summary>
internal sealed record RecalcPlan(IReadOnlyList<int> Order, IReadOnlySet<int> CycleMembers);

/// <summary>
/// Plans a recalculation with an iterative (explicit-stack) Tarjan SCC over <see cref="DependencyGraph.DirectDependents"/>,
/// edges running precedent → dependent. Tarjan's DFS from the roots visits exactly the affected set
/// (the changed formula cells plus all transitive dependents), so collecting the set and finding SCCs is one pass.
/// No call-stack recursion is used (§6.7).
/// </summary>
internal sealed class RecalcPlanner(DependencyGraph g)
{
    public RecalcPlan Plan(IEnumerable<int> changedKeys, IEnumerable<string> changedNames, Func<int, bool> isFormula)
    {
        // Roots are deduplicated; non-formula changes are expanded in bulk so each shared range entry
        // or name node is visited once however many changed cells it covers.
        var rootSet = new HashSet<int>();
        var roots = new List<int>();
        var nonFormula = new List<int>();
        foreach (int k in changedKeys)
        {
            if (!isFormula(k)) nonFormula.Add(k);
            else if (rootSet.Add(k)) roots.Add(k);
        }
        foreach (int d in g.DistinctDirectDependentsOfMany(nonFormula))
            if (rootSet.Add(d)) roots.Add(d);
        foreach (string n in changedNames)
            foreach (int d in g.NameDependents(n))
                if (rootSet.Add(d)) roots.Add(d);
        return Run(roots, restrictTo: null);
    }

    public RecalcPlan PlanAll(IEnumerable<int> allFormulaKeys)
    {
        var all = allFormulaKeys as IReadOnlyCollection<int> ?? allFormulaKeys.ToList();
        var set = all as IReadOnlySet<int> ?? new HashSet<int>(all);
        return Run(all, set);
    }

    private struct Frame(int node, IEnumerator<int> edges)
    {
        public readonly int Node = node;
        public readonly IEnumerator<int> Edges = edges;
    }

    private RecalcPlan Run(IEnumerable<int> roots, IReadOnlySet<int>? restrictTo)
    {
        // Per-node Tarjan state, keyed by dense id.
        var id = new Dictionary<int, int>();
        var index = new List<int>();
        var low = new List<int>();
        var onStack = new List<bool>();

        var sccStack = new Stack<int>();       // cell keys
        var frames = new List<Frame>();
        var reversed = new List<int>();        // cells in SCC emission order (reverse topological)
        var cycles = new HashSet<int>();
        var scc = new List<int>();

        int Visit(int key)
        {
            int i = index.Count;
            id[key] = i;
            index.Add(i);
            low.Add(i);
            onStack.Add(true);
            sccStack.Push(key);
            frames.Add(new Frame(key, g.DirectDependents(key).GetEnumerator()));
            return i;
        }

        foreach (int root in roots)
        {
            if (id.ContainsKey(root)) continue;
            Visit(root);
            while (frames.Count > 0)
            {
                var f = frames[^1];
                int v = id[f.Node];
                if (f.Edges.MoveNext())
                {
                    int w = f.Edges.Current;
                    if (restrictTo is not null && !restrictTo.Contains(w)) continue;
                    if (w == f.Node) { cycles.Add(w); continue; }
                    if (!id.TryGetValue(w, out int wi)) Visit(w);
                    else if (onStack[wi] && index[wi] < low[v]) low[v] = index[wi];
                    continue;
                }

                f.Edges.Dispose();
                frames.RemoveAt(frames.Count - 1);
                if (low[v] == index[v])
                {
                    scc.Clear();
                    int x;
                    do
                    {
                        x = sccStack.Pop();
                        onStack[id[x]] = false;
                        scc.Add(x);
                    } while (x != f.Node);
                    if (scc.Count > 1) cycles.UnionWith(scc);
                    // Within an SCC order is irrelevant; push in pop order and reverse later.
                    reversed.AddRange(scc);
                }
                if (frames.Count > 0)
                {
                    int p = id[frames[^1].Node];
                    if (low[v] < low[p]) low[p] = low[v];
                }
            }
        }

        reversed.Reverse();
        return new RecalcPlan(reversed, cycles);
    }
}
