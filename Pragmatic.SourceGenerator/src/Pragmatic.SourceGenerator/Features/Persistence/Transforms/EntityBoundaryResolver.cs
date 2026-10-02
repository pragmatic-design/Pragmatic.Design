using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Fills in the boundary of every entity of this compilation, and reports the cases where the
///     answer cannot be found.
/// </summary>
/// <remarks>
///     <para>
///         Three sources, in order: <c>[BelongsTo&lt;TBoundary&gt;]</c> written on the entity, the
///         boundary that claims it with <c>[Owns&lt;TEntity&gt;]</c>, and — when the assembly declares
///         exactly one boundary — that one, which owns everything.
///     </para>
///     <para>
///         It runs here and not in <c>EntityTransform</c> because the answer needs the whole
///         compilation: a per-symbol transform sees one entity and no boundaries at all.
///     </para>
/// </remarks>
internal static class EntityBoundaryResolver
{
    /// <summary>
    ///     Returns the entities with their boundary resolved, plus what could not be resolved.
    /// </summary>
    public static (ImmutableArray<EntityMetadataModel> Entities, ImmutableArray<BoundaryOwnershipProblem> Problems)
        Resolve(ImmutableArray<EntityMetadataModel> entities, BoundaryOwnership ownership)
    {
        var boundaries = ownership.Boundaries.AsImmutableArray();
        if (boundaries.IsDefaultOrEmpty)
            return (entities, ImmutableArray<BoundaryOwnershipProblem>.Empty);

        var problems = ImmutableArray.CreateBuilder<BoundaryOwnershipProblem>();
        var result = ImmutableArray.CreateBuilder<EntityMetadataModel>(entities.Length);

        foreach (var entity in entities)
        {
            // A referenced assembly already resolved its own entities.
            if (entity.IsFromReference)
            {
                result.Add(entity);
                continue;
            }

            // EntityMetadataModel.FullTypeName is ToDisplayString(); the attribute's type argument is
            // read fully qualified. Two spellings of one name, so the comparison normalises both.
            var entityName = StripGlobal(entity.FullTypeName);
            var claimants = boundaries
                .Where(b => b.OwnedEntities.AsImmutableArray().Any(o => StripGlobal(o) == entityName))
                .ToImmutableArray();

            // Counted before the pre-filled answer is trusted: the per-symbol reader takes the FIRST
            // [Owns<T>] it meets, so an entity two boundaries claim arrives here with a boundary already
            // set, and the double claim is only visible from this side.
            if (claimants.Length > 1)
            {
                problems.Add(new BoundaryOwnershipProblem(
                    BoundaryOwnershipProblemKind.ClaimedByTwo, entity.TypeName,
                    claimants[0].ShortName, claimants[1].ShortName, entity.DeclarationLocation));
                result.Add(entity);
                continue;
            }

            // Declared on the entity, or already answered by BoundaryOwnershipReader from [Owns<T>].
            if (!string.IsNullOrEmpty(entity.BoundaryTypeFullName))
            {
                result.Add(entity);
                continue;
            }

            var owner = claimants.Length == 1
                ? claimants[0]
                : boundaries.Length == 1
                    ? boundaries[0]
                    : null;

            if (owner is null)
            {
                problems.Add(new BoundaryOwnershipProblem(
                    BoundaryOwnershipProblemKind.ClaimedByNone, entity.TypeName,
                    boundaries.Length.ToString(), null, entity.DeclarationLocation));
                result.Add(entity);
                continue;
            }

            result.Add(entity with
            {
                BoundaryName = owner.ShortName,
                BoundaryTypeFullName = owner.FullTypeName
            });
        }

        return (result.ToImmutable(), problems.ToImmutable());
    }

    private static string StripGlobal(string name)
        => name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring(8) : name;

    /// <summary>Maps a problem to the descriptor that reports it.</summary>
    public static Microsoft.CodeAnalysis.DiagnosticDescriptor DescriptorFor(BoundaryOwnershipProblemKind kind)
        => kind == BoundaryOwnershipProblemKind.ClaimedByTwo
            ? PersistenceDiagnostics.EntityClaimedByTwoBoundaries
            : PersistenceDiagnostics.EntityClaimedByNoBoundary;
}

/// <summary>What went wrong deciding which boundary owns an entity.</summary>
internal enum BoundaryOwnershipProblemKind
{
    ClaimedByNone,
    ClaimedByTwo
}

/// <summary>A cache-safe description of one ownership problem, reported in the source-output stage.</summary>
internal sealed record BoundaryOwnershipProblem(
    BoundaryOwnershipProblemKind Kind,
    string EntityName,
    string? First,
    string? Second,
    LocationInfo? LocationInfo);
