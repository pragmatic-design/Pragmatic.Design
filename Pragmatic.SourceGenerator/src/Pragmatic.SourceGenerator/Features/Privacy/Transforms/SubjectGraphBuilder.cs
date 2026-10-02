using Pragmatic.SourceGenerator.Features.Privacy.Models;

namespace Pragmatic.SourceGenerator.Features.Privacy.Transforms;

/// <summary>
///     Works out which entities are reachable from a data subject, by following the declared paths.
/// </summary>
/// <remarks>
///     Reachability is what bounds the whole analysis. Nothing is reported and nothing is generated for
///     an entity no subject leads to — which is what keeps a solution that has not declared a
///     <c>[DataSubject]</c> completely quiet, and what keeps the blast radius of the first one to the
///     graph around it rather than the whole codebase.
/// </remarks>
internal static class SubjectGraphBuilder
{
    /// <summary>
    ///     Returns the entities reachable from any <c>[DataSubject]</c>, subjects included.
    /// </summary>
    public static IReadOnlyList<PrivacyEntityModel> Reachable(IReadOnlyList<PrivacyEntityModel> entities)
    {
        if (entities.Count == 0)
            return [];

        var subjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entities)
            if (e.IsSubject)
                subjects.Add(e.FullTypeName);

        // No subject declared means the analysis is switched off. Returning early here — rather than
        // letting the rest run and produce nothing — is what makes "opted out" cost nothing.
        if (subjects.Count == 0)
            return [];

        var byName = new Dictionary<string, PrivacyEntityModel>(StringComparer.Ordinal);
        foreach (var e in entities)
            byName[e.FullTypeName] = e;

        var reachable = new List<PrivacyEntityModel>();
        foreach (var entity in entities)
            if (LeadsToASubject(entity, subjects, byName))
                reachable.Add(entity);

        return reachable;
    }

    /// <summary>
    ///     Composes the hops from an entity to its subject, or null when there is no route.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Null is the answer for an entity that leads nowhere, and it has to stay null: an adapter
    ///         built on a route that stops short would filter on nothing and hand one subject every
    ///         other subject's rows. No adapter is a gap; that one is a disclosure.
    ///     </para>
    ///     <para>
    ///         Cycle-safe by the same visited set the reachability walk uses. Two entities pointing at
    ///         each other is an ordinary intermediate state while somebody wires the paths up, and an
    ///         IDE that hangs there is worse than one that reports nothing yet.
    ///     </para>
    /// </remarks>
    public static SubjectRoute? RouteTo(
        PrivacyEntityModel entity, IReadOnlyList<PrivacyEntityModel> entities)
    {
        var byName = new Dictionary<string, PrivacyEntityModel>(StringComparer.Ordinal);
        foreach (var e in entities)
            byName[e.FullTypeName] = e;

        var navigations = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { entity.FullTypeName };
        var current = entity;

        while (!current.IsSubject)
        {
            if (current.SubjectPath is not { } hop || current.SubjectPathTargetFullTypeName is not { } next)
                return null;

            if (!visited.Add(next) || !byName.TryGetValue(next, out var target))
                return null;

            navigations.Add(hop);
            current = target;
        }

        var identifier = current.SubjectIdentifier!;
        var declared = current.Properties.FirstOrDefault(p => p.Name == identifier);

        // The named property has to exist and be public: the adapter reads it through a LINQ expression
        // the provider translates, which reaches nothing else.
        if (declared is null)
            return null;

        return new SubjectRoute
        {
            Navigations = navigations,
            IdentifierProperty = identifier,
            IdentifierTypeDisplay = declared.TypeDisplay,
            SubjectFullTypeName = current.FullTypeName
        };
    }

    /// <summary>
    ///     Walks the declared path until it reaches a subject, runs out of path, or comes back on itself.
    /// </summary>
    private static bool LeadsToASubject(
        PrivacyEntityModel entity,
        HashSet<string> subjects,
        Dictionary<string, PrivacyEntityModel> byName)
    {
        if (entity.IsSubject)
            return true;

        // A cycle is a modelling mistake, not something to crash on: two entities can easily end up
        // pointing at each other while someone is still wiring the paths up, and an IDE that hangs
        // during that is worse than one that reports nothing yet.
        var visited = new HashSet<string>(StringComparer.Ordinal) { entity.FullTypeName };
        var current = entity;

        while (current.SubjectPathTargetFullTypeName is { } next)
        {
            if (subjects.Contains(next))
                return true;

            if (!visited.Add(next) || !byName.TryGetValue(next, out var target))
                return false;

            current = target;
        }

        return false;
    }
}
