using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Validation;

/// <summary>
///     Reports PRAG0706: a <c>[ReadAccess&lt;TEntity&gt;]</c> whose target entity is owned by a boundary
///     the host maps to a different database.
///     <para>
///     <c>DbContextFeature.BuildReadAccessEntities</c> turns each declaration into a <c>DbSet</c> on the
///     reading boundary's DbContext, plus <c>ApplyConfiguration</c> and
///     <c>ToTable(t =&gt; t.ExcludeFromMigrations())</c>. The exclusion is what makes the split fatal: the
///     table is created only by the OWNING database's MigrationDbContext, so the reading DbContext — bound
///     to a different connection string — queries a table that does not exist there. Nothing fails at
///     compile time.
///     </para>
/// </summary>
/// <remarks>
///     Calibration — reported only when the generated code genuinely cannot work:
///     <list type="bullet">
///         <item><description>
///             <b>No topology</b> (<c>HasTopology == false</c>): every boundary shares the single
///             MigrationDbContext, so no split exists.
///         </description></item>
///         <item><description>
///             <b>Either side unassigned</b>: a boundary missing from <c>BoundaryToDatabase</c> falls into
///             the catch-all MigrationDbContext; which database it ends up on is not decided here, and
///             PRAG1651 already covers an include without a database.
///         </description></item>
///         <item><description>
///             <b>Unresolvable target</b>: <c>BuildReadAccessEntities</c> skips a type it cannot find in the
///             entity set, so no DbSet is generated and there is no broken join to warn about.
///         </description></item>
///         <item><description>
///             <b>Same connection string</b>: two <c>[PragmaticDatabase]</c> classes may carry the same
///             <c>ConfigKey</c> and therefore address the same physical database. Matching config keys win
///             over differing type names — the join really does work.
///         </description></item>
///     </list>
/// </remarks>
internal static class ReadAccessTopologyValidator
{
    /// <summary>
    ///     Validates every <c>[ReadAccess]</c> declaration against the host database topology.
    /// </summary>
    public static void Validate(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities,
        EquatableDictionary<string, EquatableArray<string>> readAccessByBoundary,
        DatabaseTopologyInfo topology)
    {
        if (!topology.HasTopology || readAccessByBoundary.IsEmpty || entities.IsDefaultOrEmpty)
            return;

        var databaseByBoundary = new Dictionary<string, DatabaseAssignment>(StringComparer.Ordinal);
        foreach (var kvp in topology.BoundaryToDatabase)
            databaseByBoundary[Normalize(kvp.Key)] = kvp.Value;

        // Entity FQN → owning boundary. Only valid entities: an invalid model carries no usable boundary.
        var ownerByEntity = new Dictionary<string, EntityMetadataModel>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            if (entity.IsValid && !string.IsNullOrEmpty(entity.BoundaryTypeFullName))
                ownerByEntity[Normalize(entity.FullTypeName)] = entity;
        }

        foreach (var kvp in readAccessByBoundary)
        {
            var readerBoundary = Normalize(kvp.Key);
            if (!databaseByBoundary.TryGetValue(readerBoundary, out var readerDatabase))
                continue;

            foreach (var targetTypeName in kvp.Value)
            {
                if (!ownerByEntity.TryGetValue(Normalize(targetTypeName), out var target))
                    continue;

                var ownerBoundary = Normalize(target.BoundaryTypeFullName!);
                if (ownerBoundary == readerBoundary)
                    continue;
                if (!databaseByBoundary.TryGetValue(ownerBoundary, out var ownerDatabase))
                    continue;
                if (IsSameDatabase(readerDatabase, ownerDatabase))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    ReadAccessDiagnostics.ReadAccessCrossDatabase,
                    readerDatabase.Location?.ToLocation() ?? ownerDatabase.Location?.ToLocation() ?? Location.None,
                    SimpleName(readerBoundary),
                    target.TypeName,
                    readerDatabase.DatabaseClassName,
                    SimpleName(ownerBoundary),
                    ownerDatabase.DatabaseClassName));
            }
        }
    }

    /// <summary>
    ///     Two assignments address the same physical database when their connection-string config keys
    ///     match; otherwise the database type identifies them.
    /// </summary>
    private static bool IsSameDatabase(DatabaseAssignment left, DatabaseAssignment right)
    {
        if (!string.IsNullOrEmpty(left.ConfigKey) && !string.IsNullOrEmpty(right.ConfigKey))
            return string.Equals(left.ConfigKey, right.ConfigKey, StringComparison.Ordinal);

        return string.Equals(Normalize(left.DatabaseTypeName), Normalize(right.DatabaseTypeName),
            StringComparison.Ordinal);
    }

    private static string Normalize(string typeName)
        => typeName.StartsWith("global::", StringComparison.Ordinal) ? typeName.Substring(8) : typeName;

    private static string SimpleName(string fullTypeName)
    {
        var lastDot = fullTypeName.LastIndexOf('.');
        return lastDot >= 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
    }
}
