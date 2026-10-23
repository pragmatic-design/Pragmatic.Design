using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Checks the persistence engine of every entity this compilation knows, and returns the choices that cannot
///     stand.
/// </summary>
/// <remarks>
///     <para>
///         An entity's engine is the one its boundary names with <c>[PersistenceEngine]</c>
///         (<see cref="EntityMetadataModel.NamedPersistenceEngine" />); without a marker, the only engine the
///         compilation references. An engine is referenced when its bridge is
///         (<see cref="PersistenceEngineMarker.Referenced" />).
///     </para>
///     <para>
///         ⚠️ Every entity, not only the ones declared here. A module that references one engine has nothing to
///         choose between, so it reports nothing; the host that references two is the first compilation where the
///         choice exists, and the entity reached it through the module's metadata. A compilation that references
///         no engine reports nothing: a module that leaves the engine to its host has nothing to check the marker
///         against.
///     </para>
/// </remarks>
internal static class PersistenceEngineChoiceReader
{
    public static EquatableArray<PersistenceEngineProblem> Read(
        ImmutableArray<EntityMetadataModel> entities, BoundaryOwnership ownership, DetectedFeatures features)
    {
        var referenced = PersistenceEngineMarker.Referenced(features);
        if (referenced.IsEmpty)
            return EquatableArray<PersistenceEngineProblem>.Empty;

        var referencedText = string.Join(", ", referenced);
        var problems = ImmutableArray.CreateBuilder<PersistenceEngineProblem>();
        var reportedBoundaries = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            // A package nobody adopted reaches no DbContext and no schema, so no engine either.
            if (entity.IsFromReference && string.IsNullOrEmpty(entity.BoundaryTypeFullName))
                continue;

            // Normalised: the per-symbol transforms write the display string, EntityBoundaryResolver the
            // fully qualified name of the boundary it found, and one boundary must be reported once.
            var boundary = string.IsNullOrEmpty(entity.BoundaryTypeFullName)
                ? "(none)"
                : StripGlobal(entity.BoundaryTypeFullName!);

            if (entity.NamedPersistenceEngine is { } engine)
            {
                // Once per boundary, where the fix is: every entity of that boundary has the same problem.
                if (!referenced.Contains(engine) && reportedBoundaries.Add(boundary))
                    problems.Add(new PersistenceEngineProblem(
                        PersistenceEngineProblemKind.EngineNotReferenced,
                        boundary,
                        boundary,
                        engine,
                        referencedText,
                        LocationOf(boundary, ownership)));
            }
            else if (referenced.Length >= 2)
            {
                problems.Add(new PersistenceEngineProblem(
                    PersistenceEngineProblemKind.NoEngineNamed,
                    entity.FullTypeName,
                    boundary,
                    null,
                    referencedText,
                    entity.DeclarationLocation));
            }
        }

        return problems.ToImmutable();
    }

    /// <summary>
    ///     Where a boundary of this assembly is declared; null for one in a referenced assembly, which has no source
    ///     here to point at.
    /// </summary>
    private static LocationInfo? LocationOf(string boundary, BoundaryOwnership ownership)
    {
        foreach (var declared in ownership.Boundaries)
        {
            if (StripGlobal(declared.FullTypeName) == boundary)
                return declared.LocationInfo;
        }

        return null;
    }

    private static string StripGlobal(string name)
        => name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring(8) : name;
}
