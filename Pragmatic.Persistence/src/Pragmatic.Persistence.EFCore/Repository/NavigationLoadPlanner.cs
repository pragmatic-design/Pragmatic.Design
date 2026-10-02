using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Pragmatic.Persistence.EFCore.Repository;

/// <summary>
///     Works out which navigation paths of a tracked graph are <b>not</b> loaded yet.
/// </summary>
/// <remarks>
///     <para>
///         A merge decides what to remove by looking at what is there, and EF cannot tell an empty
///         collection from one that was never loaded. An invoker that reads the aggregate itself
///         includes the paths it writes; one handed an entity by a caller — a domain action that
///         already read the row — has no idea how much of the graph came with it.
///     </para>
///     <para>
///         Skipping the load there is silent data loss: the merge sees nothing, so it removes nothing
///         and adds everything. Loading everything again is
///         the opposite mistake — a round trip for rows already in hand.
///     </para>
///     <para>
///         This asks the change tracker instead, which costs no query, and reports only the paths
///         that are missing. The caller then reads <b>once</b> with exactly those includes: zero
///         queries when the graph is already complete, one when it is not, never one per row.
///     </para>
/// </remarks>
public static class NavigationLoadPlanner
{
    /// <summary>
    ///     The subset of <paramref name="paths" /> that is not fully loaded on <paramref name="root" />.
    /// </summary>
    /// <param name="context">The context tracking the graph.</param>
    /// <param name="root">The entity the paths are relative to.</param>
    /// <param name="paths">Dotted navigation paths, e.g. <c>Lines.Allocations</c>.</param>
    /// <returns>
    ///     The paths still to load, in the order given. Empty when everything asked for is already
    ///     there — which is the case worth optimising, because it is the common one.
    /// </returns>
    /// <remarks>
    ///     A path counts as missing when <b>any</b> entity along it has that navigation unloaded: a
    ///     collection loaded on one parent and not on its sibling is not loaded, and merging against
    ///     it would drop the sibling's rows.
    /// </remarks>
    public static IReadOnlyList<string> MissingPaths(
        DbContext context, object root, IReadOnlyList<string> paths)
    {
        Ensure.Ensure.ThrowIfNull(context);
        Ensure.Ensure.ThrowIfNull(root);

        if (paths is null || paths.Count == 0)
            return [];

        var missing = new List<string>();

        foreach (var path in paths)
        {
            if (!IsFullyLoaded(context, root, path.Split('.')))
                missing.Add(path);
        }

        return missing;
    }

    /// <summary>Walks one path level by level, widening to every entity found at each step.</summary>
    private static bool IsFullyLoaded(DbContext context, object root, string[] segments)
    {
        var level = new List<object> { root };

        foreach (var segment in segments)
        {
            var next = new List<object>();

            foreach (var entity in level)
            {
                // An entity the context does not track cannot answer, and guessing "loaded" here is
                // the answer that loses rows. Treat it as missing and let the caller read.
                var entry = context.Entry(entity);
                if (entry.State == EntityState.Detached)
                    return false;

                var navigation = entry.Navigations
                    .FirstOrDefault(n => n.Metadata.Name == segment);

                // Not a navigation at all: nothing to load, and nothing this planner can fix. Saying
                // "missing" would ask for an Include that EF would reject.
                if (navigation is null)
                    return true;

                if (!navigation.IsLoaded)
                    return false;

                switch (navigation)
                {
                    case CollectionEntry { CurrentValue: { } items }:
                        foreach (var item in items)
                            next.Add(item);
                        break;

                    case ReferenceEntry { CurrentValue: { } value }:
                        next.Add(value);
                        break;
                }
            }

            // Nothing at this level: the rest of the path has nothing to hang from, and is loaded by
            // vacuity rather than by omission.
            if (next.Count == 0)
                return true;

            level = next;
        }

        return true;
    }
}
