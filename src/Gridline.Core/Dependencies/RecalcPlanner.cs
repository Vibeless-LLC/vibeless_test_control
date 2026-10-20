namespace Gridline.Core.Dependencies;

/// <summary>
/// Formula cells to evaluate, each once, precedents before dependents (§6.7). Cycle members are included
/// where their strongly connected component falls and are also listed in <see cref="CycleMembers"/>.
/// </summary>
internal sealed record RecalcPlan(IReadOnlyList<long> Order, IReadOnlySet<long> CycleMembers);

/// <summary>
/// Plans a recalculation with an iterative (explicit-stack) Tarjan SCC over <see cref="DependencyGraph.DirectDependents"/>,
/// edges running precedent → dependent. Tarjan's DFS from the roots visits exactly the affected set
/// (the changed formula cells plus all transitive dependents), so collecting the set and finding SCCs is one pass.
/// No call-stack recursion is used (§6.7).
/// </summary>
internal sealed class RecalcPlanner(DependencyGraph g)
{
    /// <summary>
    /// Plans the recalc after <paramref name="changedKeys"/> (cells whose input changed) and <paramref name="changedNames"/>
    /// (upper-cased names whose definition changed). <paramref name="extraRoots"/> are formula cells to re-evaluate
    /// regardless (e.g. formulas rebound to a sheet that started or stopped existing, CR1.2).
    /// </summary>
    public RecalcPlan Plan(IEnumerable<long> changedKeys, IEnumerable<string> changedNames, IEnumerable<long> extraRoots, Func<long, bool> isFormula)
    {
        // Roots are deduplicated; non-formula changes are expanded in bulk so each shared range entry
        // or name node is visited once however many changed cells it covers.
        var rootSet = new HashSet<long>();
        var roots = new List<long>();
        var nonFormula = new List<long>();
        foreach (long k in changedKeys)
        {
            if (!isFormula(k)) nonFormula.Add(k);
            else if (rootSet.Add(k)) roots.Add(k);
        }
        foreach (long k in extraRoots)
            if (rootSet.Add(k)) roots.Add(k);
        foreach (long d in g.DistinctDirectDependentsOfMany(nonFormula))
            if (rootSet.Add(d)) roots.Add(d);
        foreach (string n in changedNames)
            foreach (long d in g.NameDependents(n))
                if (rootSet.Add(d)) roots.Add(d);
        return Run(roots, restrictTo: null);
    }

    public RecalcPlan PlanAll(IEnumerable<long> allFormulaKeys)
    {
        var all = allFormulaKeys as IReadOnlyCollection<long> ?? allFormulaKeys.ToList();
        var set = all as IReadOnlySet<long> ?? new HashSet<long>(all);
        return Run(all, set);
    }

    private struct Frame(long node, IEnumerator<long> edges)
    {
        public readonly long Node = node;
        public readonly IEnumerator<long> Edges = edges;
    }

    private RecalcPlan Run(IEnumerable<long> roots, IReadOnlySet<long>? restrictTo)
    {
        // Per-node Tarjan state, keyed by dense id.
        var id = new Dictionary<long, int>();
        var index = new List<int>();
        var low = new List<int>();
        var onStack = new List<bool>();

        var sccStack = new Stack<long>();      // global keys
        var frames = new List<Frame>();
        var reversed = new List<long>();       // cells in SCC emission order (reverse topological)
        var cycles = new HashSet<long>();
        var scc = new List<long>();

        int Visit(long key)
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

        foreach (long root in roots)
        {
            if (id.ContainsKey(root)) continue;
            Visit(root);
            while (frames.Count > 0)
            {
                var f = frames[^1];
                int v = id[f.Node];
                if (f.Edges.MoveNext())
                {
                    long w = f.Edges.Current;
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
                    long x;
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
