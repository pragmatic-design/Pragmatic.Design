using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The modules' tables in the Sql engine's schema (<see cref="PragmaticSchemas" />): the rows their engines packages declare, by name, and
///     the indexes EF Core's configuration gives each table, which an attribute of the row cannot describe (several columns, a filter).
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    /// <summary>The event outbox's row on the Sql engine (Pragmatic.Events.Engines): its table is EF Core's <c>__EventOutbox</c>, column for column.</summary>
    private const string EventOutboxRowType = "Pragmatic.Events.Engines.Sql.EventOutboxRow";

    /// <summary>
    ///     The outbox's table with the index its delivery's claim reads (pending, re-claimable, oldest first), named as EF Core's configuration
    ///     names it: an attribute of the row describes no index over several columns.
    /// </summary>
    private static TableSchemaModel WithClaimIndex(TableSchemaModel table)
        => table with { Indexes = table.Indexes.Append(new IndexSchemaModel($"IX_{table.Name}_ProcessedAt_ClaimedUntil_CreatedAt", ["ProcessedAt", "ClaimedUntil", "CreatedAt"], false, null)).ToEquatableArray() };

    private const string MessagingOutboxRowType = "Pragmatic.Messaging.Engines.Sql.OutboxMessageRow";

    private const string AuditEntryRowType = "Pragmatic.Audit.Engines.Sql.AuditEntryRow";
    private const string AuditSegmentRowType = "Pragmatic.Audit.Engines.Sql.AuditSegmentRow";
    private const string AuditPrunedRangeRowType = "Pragmatic.Audit.Engines.Sql.AuditPrunedRangeRow";

    /// <summary>
    ///     The trail's entries with the indexes EF Core's schema gives them (<c>SchemaMetadataTransform</c>): a segment in order, a subject over
    ///     time, time, and a request's correlation.
    /// </summary>
    private static TableSchemaModel WithAuditEntryIndexes(TableSchemaModel table)
        => table with
        {
            Indexes = table.Indexes
                .Append(new IndexSchemaModel($"IX_{table.Name}_SegmentId_Seq", ["SegmentId", "Seq"], false, null))
                .Append(new IndexSchemaModel($"IX_{table.Name}_SubjectRef_OccurredAt", ["SubjectRef", "OccurredAt"], false, null))
                .Append(new IndexSchemaModel($"IX_{table.Name}_OccurredAt", ["OccurredAt"], false, null))
                .Append(new IndexSchemaModel($"IX_{table.Name}_CorrelationId", ["CorrelationId"], false, null))
                .ToEquatableArray(),
        };

    /// <summary>The trail's segments with the index verification walks sealed segments by.</summary>
    private static TableSchemaModel WithAuditSegmentIndexes(TableSchemaModel table)
        => table with { Indexes = table.Indexes.Append(new IndexSchemaModel($"IX_{table.Name}_SealedAt", ["SealedAt"], false, null)).ToEquatableArray() };

    private const string SubjectRowType = PragmaticRegistration.EngineSubjectRegistry;
    private const string ConsentRowType = "Pragmatic.Privacy.Engines.Sql.ConsentRow";

    /// <summary>
    ///     The registry's subjects with EF Core's unique blind index (<c>SchemaMetadataTransform.SubjectRegistry</c>): two live subjects cannot
    ///     share one. A forgotten subject's is null, which a unique index does not compare, except on SQL Server, where it counts as a value:
    ///     there the index holds the rows that have one.
    /// </summary>
    private static TableSchemaModel WithSubjectIndexes(TableSchemaModel table, SqlDialect dialect)
        => table with
        {
            Indexes = table.Indexes
                .Append(new IndexSchemaModel($"IX_{table.Name}_LookupIndex", ["LookupIndex"], true, dialect == SqlDialect.SqlServer ? "\"LookupIndex\" IS NOT NULL" : null))
                .ToEquatableArray(),
        };

    /// <summary>
    ///     The subjects' keys with EF Core's unique index on the key id (<c>SchemaMetadataTransform.BuildSubjectKeysTable</c>): a reader holding only
    ///     the ciphertext resolves by it, and two rows sharing one would mean a reused key.
    /// </summary>
    private static TableSchemaModel WithSubjectKeyIndexes(TableSchemaModel table)
        => table with { Indexes = table.Indexes.Append(new IndexSchemaModel($"IX_{table.Name}_KeyId", ["KeyId"], true, null)).ToEquatableArray() };

    /// <summary>The consents with the index "what has this subject agreed to" reads.</summary>
    private static TableSchemaModel WithConsentIndexes(TableSchemaModel table)
        => table with { Indexes = table.Indexes.Append(new IndexSchemaModel($"IX_{table.Name}_SubjectRef", ["SubjectRef"], false, null)).ToEquatableArray() };

    /// <summary>
    ///     The tables of the packages imported into a boundary the engine serves whose rows live in that boundary's database
    ///     (<see cref="Core.PackageStores" />): in its database, with no index of their own, as EF Core maps the package's entities there.
    /// </summary>
    private static void PackageTables(Compilation compilation, PragmaticEngines engines, Func<INamedTypeSymbol, string?> databaseOf,
        Action<string, HashSet<string>, Func<TableSchemaModel, TableSchemaModel>> tableIn, CancellationToken ct)
    {
        foreach (var import in EntityMetadataReader.ReadPackageAdoptions(compilation, ct))
        {
            if (Core.PackageStores.OfPackage(import.Key) is not { } store || !engines.ServesBoundary(import.Value, PragmaticEngine.Sql)
                || databaseOf(import.Value) is not { } database)
                continue;

            foreach (var row in store.SqlRows)
                tableIn(row, new HashSet<string>(StringComparer.Ordinal) { database }, static table => table);
        }
    }

    /// <summary>
    ///     The saga instances' table with EF Core's three indexes (<c>SagaEntityTypeConfiguration</c>): at most one active saga per type,
    ///     correlation and tenant, the active sagas of a type, and the timeout scan. The unique one holds the active rows only (<c>"Status" = 0</c>),
    ///     a partial index MySQL and Oracle do not have: there it is left out, and the database does not refuse a second active saga
    ///     (owner decision, 2026-11-05: filtered where the database can express it; <see cref="Diagnostics.SagaUniquenessLeftOut" /> says so).
    /// </summary>
    /// <remarks>
    ///     On Oracle, which stores the empty string as NULL, the tenant (empty in a single-tenant application) is a nullable column: a NOT NULL
    ///     one would refuse every saga of an application without tenants. The unique index that reads it is left out there anyway.
    /// </remarks>
    private static TableSchemaModel WithSagaInstanceIndexes(TableSchemaModel table, SqlDialect dialect)
    {
        if (dialect == SqlDialect.Oracle)
            table = table with { Columns = table.Columns.Select(c => c.Name == "TenantId" ? c with { Nullable = true } : c).ToEquatableArray() };
        var indexes = table.Indexes.ToList();
        if (!PartialIndexLeftOut(dialect))
            indexes.Add(new IndexSchemaModel($"IX_{table.Name}_SagaType_CorrelationId_TenantId", ["SagaType", "CorrelationId", "TenantId"], true, "\"Status\" = 0"));
        indexes.Add(new IndexSchemaModel($"IX_{table.Name}_Status_SagaType", ["Status", "SagaType"], false, null));
        indexes.Add(new IndexSchemaModel($"IX_{table.Name}_Status_TimeoutAt", ["Status", "TimeoutAt"], false, null));
        return table with { Indexes = indexes.ToEquatableArray() };
    }

    /// <summary>
    ///     True on the dialects with no partial index, where a filtered unique index is left out of the schema: the active sagas' (PRAG3038) and a
    ///     <c>MaxActive = 1</c> entity's one open stretch per parent (PRAG3039).
    /// </summary>
    internal static bool PartialIndexLeftOut(SqlDialect dialect) => dialect is SqlDialect.MySql or SqlDialect.Oracle;

    /// <summary>
    ///     <see cref="Diagnostics.SagaUniquenessLeftOut" /> for each <c>[EnableSagaPersistence]</c> boundary the Sql engine serves, in a host whose
    ///     schema is typed for MySQL or Oracle: where the schema leaves the index out.
    /// </summary>
    internal static void ReportSagaUniquenessLeftOut(SourceProductionContext spc, Compilation compilation, DetectedFeatures features)
    {
        if (!UsesSql(compilation) || !features.IsHostMode || compilation.GetTypeByMetadataName(SchemaVersionType) is null
            || compilation.GetTypeByMetadataName(PragmaticRegistration.EngineSagaStore) is null
            || SchemaDialect(compilation, features) is not { } dialect || !PartialIndexLeftOut(dialect))
            return;

        var engines = PragmaticEngines.Of(compilation);
        var hasTopology = DatabaseTopologyReader.Read(compilation, spc.CancellationToken).HasTopology;
        foreach (var boundary in SchemaCandidates(compilation, hasTopology)
                     .Where(b => PragmaticRegistration.EnablesSagaPersistence(b) && engines.ServesBoundary(b, PragmaticEngine.Sql)))
            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.SagaUniquenessLeftOut, boundary.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None,
                boundary.Name, dialect == SqlDialect.MySql ? "MySQL" : "Oracle"));
    }

    /// <summary>
    ///     <see cref="Diagnostics.OpenStretchUniquenessLeftOut" /> for each <c>MaxActive = 1</c> entity the Sql engine serves, in a host whose
    ///     schema is typed for MySQL or Oracle: where the schema leaves its one-open-stretch index out.
    /// </summary>
    internal static void ReportOpenStretchUniquenessLeftOut(SourceProductionContext spc, Compilation compilation, DetectedFeatures features)
    {
        if (!UsesSql(compilation) || !features.IsHostMode || compilation.GetTypeByMetadataName(SchemaVersionType) is null
            || SchemaDialect(compilation, features) is not { } dialect || !PartialIndexLeftOut(dialect))
            return;

        var engines = PragmaticEngines.Of(compilation);
        var hasTopology = DatabaseTopologyReader.Read(compilation, spc.CancellationToken).HasTopology;
        foreach (var entity in SchemaCandidates(compilation, hasTopology).Where(t => PragmaticShape.IsPragmaticEntity(t) && engines.Serves(t, PragmaticEngine.Sql)))
        {
            if (EntityTransform.OneOpenStretchParentKey(entity) is null)
                continue;
            var parent = entity.GetAttributes().First(a => a.AttributeClass is { Name: "TemporalRelationAttribute", IsGenericType: true }).AttributeClass!.TypeArguments[0];
            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.OpenStretchUniquenessLeftOut, entity.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None,
                entity.Name, parent.Name, dialect == SqlDialect.MySql ? "MySQL" : "Oracle"));
        }
    }

    /// <summary>The executed steps' table with EF Core's foreign key to the instances (cascading) and its index.</summary>
    private static TableSchemaModel WithSagaStepKeys(TableSchemaModel table)
        => table with
        {
            Indexes = table.Indexes.Append(new IndexSchemaModel($"IX_{table.Name}_SagaInstanceId", ["SagaInstanceId"], false, null)).ToEquatableArray(),
            ForeignKeys = table.ForeignKeys.Append(new ForeignKeySchemaModel($"FK_{table.Name}___SagaInstances_SagaInstanceId", "SagaInstanceId", "__SagaInstances", "Id", "Cascade"))
                .ToEquatableArray(),
        };

    /// <summary>
    ///     The notification records' table with EF Core's four indexes (<c>NotificationEntityTypeConfiguration</c>): the status, the creation
    ///     time, the tenant, and the pending records oldest first, which the delivery worker reads.
    /// </summary>
    private static TableSchemaModel WithNotificationIndexes(TableSchemaModel table)
        => table with
        {
            Indexes = table.Indexes
                .Append(new IndexSchemaModel($"IX_{table.Name}_Status", ["Status"], false, null))
                .Append(new IndexSchemaModel($"IX_{table.Name}_CreatedAt", ["CreatedAt"], false, null))
                .Append(new IndexSchemaModel($"IX_{table.Name}_TenantId", ["TenantId"], false, null))
                .Append(new IndexSchemaModel($"IX_{table.Name}_Status_CreatedAt", ["Status", "CreatedAt"], false, null))
                .ToEquatableArray(),
        };

    /// <summary>
    ///     The Messaging outbox's table with EF Core's two indexes (<c>OutboxEntityTypeConfiguration</c>): the one the claim reads (pending, past
    ///     its backoff, re-claimable, oldest first) and the correlation lookup.
    /// </summary>
    private static TableSchemaModel WithMessagingOutboxIndexes(TableSchemaModel table)
        => table with
        {
            Indexes = table.Indexes
                .Append(new IndexSchemaModel($"IX_{table.Name}_CorrelationId", ["CorrelationId"], false, null))
                .Append(new IndexSchemaModel($"IX_{table.Name}_ProcessedAt_NextAttemptAt_ClaimedUntil_CreatedAt", ["ProcessedAt", "NextAttemptAt", "ClaimedUntil", "CreatedAt"], false, null))
                .ToEquatableArray(),
        };
}
