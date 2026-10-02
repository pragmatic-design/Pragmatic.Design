using System.Collections.Generic;
using System.Linq;

namespace Pragmatic.Testing.SourceGenerator.Synthesis;

/// <summary>
///     Orders entities so an entity is created after the entities it references through a required foreign key
///     (#7, phase 3 — the topological half of valid-body synthesis). A CRUD create test for a child can then
///     create its parents first, capture their ids, and feed them in. A dependency cycle (rare for FKs) is
///     broken best-effort so generation never hangs.
/// </summary>
internal static class FkTopologicalSorter
{
    /// <param name="dependencies">Entity → the entities it must be created <b>after</b> (its required FK targets).</param>
    /// <returns>A creation order: every dependency precedes the entity that needs it.</returns>
    public static IReadOnlyList<string> Order(IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
    {
        var result = new List<string>();
        var visited = new HashSet<string>();
        var onStack = new HashSet<string>();

        void Visit(string node)
        {
            if (visited.Contains(node) || onStack.Contains(node))
                return; // already placed, or a cycle back-edge — skip to avoid hanging.

            onStack.Add(node);
            if (dependencies.TryGetValue(node, out var deps))
                foreach (var dependency in deps.OrderBy(d => d, System.StringComparer.Ordinal))
                    Visit(dependency);
            onStack.Remove(node);

            visited.Add(node);
            result.Add(node);
        }

        // Deterministic traversal order.
        foreach (var node in dependencies.Keys.OrderBy(k => k, System.StringComparer.Ordinal))
            Visit(node);

        return result;
    }

    /// <summary>True if the FK dependency graph contains a cycle (a circular required-FK chain).</summary>
    public static bool HasCycle(IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
    {
        var visited = new HashSet<string>();
        var onStack = new HashSet<string>();

        bool Visit(string node)
        {
            if (onStack.Contains(node))
                return true;
            if (visited.Contains(node))
                return false;

            visited.Add(node);
            onStack.Add(node);
            if (dependencies.TryGetValue(node, out var deps))
                foreach (var dependency in deps)
                    if (Visit(dependency))
                        return true;
            onStack.Remove(node);
            return false;
        }

        return dependencies.Keys.Any(Visit);
    }
}
