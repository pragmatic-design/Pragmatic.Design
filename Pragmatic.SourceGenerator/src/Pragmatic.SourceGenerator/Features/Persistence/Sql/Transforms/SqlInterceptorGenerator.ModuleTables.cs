using Pragmatic.Persistence.Sql;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

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
