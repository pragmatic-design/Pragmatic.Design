using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transforms EntityMetadataModel[] into SchemaMetadataModel by mapping properties to columns,
///     adding trait-generated columns, and building indexes and foreign keys.
/// </summary>
internal static partial class SchemaMetadataTransform
{
    /// <summary>
    ///     Context resolved from Compilation, passed as parameter to avoid static mutable state.
    /// </summary>
    internal sealed class SchemaResolutionContext
    {
        /// <summary>
        ///     Owned type properties. Key: simple type name (e.g. "LocalIdentity").
        /// </summary>
        public Dictionary<string, List<(string Name, string TypeName, bool IsNullable, bool IsEnum)>> OwnedTypeProperties { get; init; } = new();

        /// <summary>
        ///     TPH derived type properties. Key: base entity FQN.
        /// </summary>
        public Dictionary<string, List<TphDerivedInfo>> TphDerivedTypes { get; init; } = new();
    }

    internal sealed class TphDerivedInfo
    {
        public required List<(string Name, string TypeName, bool IsNullable, bool IsEnum)> Properties { get; init; }
    }

    public static SchemaMetadataModel Transform(
        ImmutableArray<EntityMetadataModel> entities,
        EfCoreProvider provider,
        string databaseName,
        string rootNamespace,
        SchemaResolutionContext? context = null,
        string? configKey = null,
        bool hasEventOutbox = false,
        bool hasSagaPersistence = false,
        bool hasMessagingOutbox = false,
        bool hasBatchProgress = false,
        bool hasPrivacyEFCore = false,
        bool hasCryptographyEFCore = false,
        bool hasJobPersistence = false,
        bool hasNotificationStore = false)
    {
        var ctx = context ?? new SchemaResolutionContext();
        var validEntities = entities.Where(e => e.IsValid && !e.IsAbstract).ToList();

        // A derived entity of a TPH hierarchy has no table: its columns are merged into the root's by
        // AddTphDerivedColumns below. Counted as a table as well, it produced one table per type — a
        // hierarchy of two derived types came out as three tables where TPH wants one, and the root's
        // table already had every column. Only derived types whose ROOT declares TPH are dropped; TPT
        // and TPC both do want a table per type.
        var entityTables = validEntities
            .Where(e => !IsTphDerived(e, entities))
            .Select(e => TransformEntity(e, provider, ctx))
            .ToList();

        // M2M implicit join tables (no explicit join entity)
        var joinTables = BuildManyToManyJoinTables(validEntities, provider);
        entityTables.AddRange(joinTables);

        // The audit trail's tables when any entity in this database is [Audited] (dual-source with the
        // EF config). Both, always together: an entry whose segment row is missing cannot be sealed.
        var auditTable = BuildAuditLogTable(validEntities, provider);
        if (auditTable is not null)
        {
            entityTables.Add(auditTable);
            entityTables.Add(BuildAuditSegmentsTable(provider));
            entityTables.Add(BuildAuditPrunedRangesTable(provider));
        }

        // The subject registry's tables when this database holds a [DataSubject] and the host references
        // Pragmatic.Privacy.EFCore — the same condition under which the generated context applies
        // PrivacyDbContext.ApplyPrivacyConfigurations (dual-source with those configurations).
        if (hasPrivacyEFCore && validEntities.Any(e => e.IsDataSubject))
        {
            entityTables.Add(BuildSubjectsTable(provider));
            entityTables.Add(BuildConsentsTable(provider));
        }

        // __SubjectKeys beside the subject registry, on the same rule: the migration that creates a
        // person creates the key that opens what is protected about them.
        if (hasCryptographyEFCore && validEntities.Any(e => e.IsDataSubject))
            entityTables.Add(BuildSubjectKeysTable(provider));

        // __EventOutbox table when a boundary on this database is [EnableEventOutbox] (dual-source with the EF config)
        if (hasEventOutbox)
            entityTables.Add(BuildEventOutboxTable(provider));

        // __SagaInstances/__SagaSteps when a boundary is [EnableSagaPersistence] (dual-source with SagaEntityTypeConfiguration)
        if (hasSagaPersistence)
        {
            entityTables.Add(BuildSagaInstancesTable(provider));
            entityTables.Add(BuildSagaStepsTable(provider));
        }

        // __OutboxMessages when a boundary is [EnableOutbox] (dual-source with OutboxEntityTypeConfiguration)
        if (hasMessagingOutbox)
            entityTables.Add(BuildOutboxMessagesTable(provider));

        // __BatchProgress when a boundary is [EnableBatchProgress] (dual-source with BatchProgressEntityTypeConfiguration)
        if (hasBatchProgress)
            entityTables.Add(BuildBatchProgressTable(provider));

        // __Jobs/__RecurringJobs when a boundary is [EnableJobPersistence] (dual-source with the two
        // configurations Pragmatic.Jobs.EFCore declares). Both, always together: a queue without its
        // schedules is a store that runs nothing, and jobs.UseEfCore() refuses to start without either.
        if (hasJobPersistence)
        {
            entityTables.Add(BuildJobsTable(provider));
            entityTables.Add(BuildRecurringJobsTable(provider));
        }

        // __Notifications when a boundary is [StoresNotifications] (dual-source with
        // NotificationEntityTypeConfiguration).
        if (hasNotificationStore)
            entityTables.Add(BuildNotificationsTable(provider));

        return new SchemaMetadataModel(databaseName, rootNamespace, provider, entityTables.ToImmutableArray(), configKey);
    }

    /// <summary>
    ///     Emits the <c>__Notifications</c> table. Mirrors
    ///     <c>NotificationEntityTypeConfiguration.Configure</c> so the migration schema and the EF
    ///     model agree.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The three enum columns are <c>HasConversion&lt;string&gt;()</c> in that configuration,
    ///         so they are <b>text</b> here and not integers — the mistake that would build, migrate and
    ///         then fail on the first insert with a type mismatch EF reports from the provider.
    ///     </para>
    ///     <para>
    ///         Without a mirror here, the table would belong to a <c>DbContext</c> of the package's own,
    ///         a composed host creates one context per declared database and nothing else, and the first
    ///         send would fail with <c>relation "__Notifications" does not
    ///         exist</c>.
    ///     </para>
    /// </remarks>
    private static TableSchemaModel BuildNotificationsTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        var guid = SqlTypeMapper.MapToSqlType("System.Guid", provider);
        var dto = SqlTypeMapper.MapToSqlType("System.DateTimeOffset", provider);
        var strMax = SqlTypeMapper.MapToSqlType("System.String", provider);
        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("Id", guid, false, true),
            // Stored by name: the configuration converts each of the three to string.
            new ColumnSchemaModel("Audience", Str(32), false, false),
            new ColumnSchemaModel("RecipientAddress", Str(512), false, false),
            new ColumnSchemaModel("Channel", Str(32), false, false),
            new ColumnSchemaModel("Subject", Str(1024), false, false),
            new ColumnSchemaModel("Status", Str(32), false, false),
            new ColumnSchemaModel("ProviderId", Str(256), true, false),
            new ColumnSchemaModel("ErrorMessage", Str(2048), true, false),
            new ColumnSchemaModel("TenantId", Str(128), true, false),
            new ColumnSchemaModel("Category", Str(256), true, false),
            new ColumnSchemaModel("CreatedAt", dto, false, false),
            new ColumnSchemaModel("SentAt", dto, true, false),
            new ColumnSchemaModel("DeliveredAt", dto, true, false),
            new ColumnSchemaModel("ReadAt", dto, true, false),
            // HasColumnType("text"): the unbounded string of the provider, which is what strMax gives.
            new ColumnSchemaModel("MetadataJson", strMax, true, false));

        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___Notifications_Status", ImmutableArray.Create("Status")),
            new IndexSchemaModel("IX___Notifications_CreatedAt", ImmutableArray.Create("CreatedAt")),
            new IndexSchemaModel("IX___Notifications_TenantId", ImmutableArray.Create("TenantId")),
            new IndexSchemaModel("IX___Notifications_Status_CreatedAt",
                ImmutableArray.Create("Status", "CreatedAt")));

        return new TableSchemaModel("__Notifications", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    /// <summary>
    ///     Emits the <c>__SagaInstances</c> table. Mirrors <c>SagaEntityTypeConfiguration.Configure(SagaInstance)</c>
    ///     so the migration schema and the EF model agree.
    /// </summary>
    private static TableSchemaModel BuildSagaInstancesTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        var guid = SqlTypeMapper.MapToSqlType("System.Guid", provider);
        var i32 = SqlTypeMapper.MapToSqlType("System.Int32", provider);
        var dto = SqlTypeMapper.MapToSqlType("System.DateTimeOffset", provider);
        var strMax = SqlTypeMapper.MapToSqlType("System.String", provider);
        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("Id", guid, false, true),
            new ColumnSchemaModel("SagaType", Str(512), false, false),
            new ColumnSchemaModel("CorrelationId", Str(128), false, false),
            new ColumnSchemaModel("State", i32, false, false),
            new ColumnSchemaModel("StateData", strMax, true, false),
            new ColumnSchemaModel("Status", i32, false, false),
            new ColumnSchemaModel("StartedAt", dto, false, false),
            new ColumnSchemaModel("CompletedAt", dto, true, false),
            new ColumnSchemaModel("LastStepAt", dto, true, false),
            new ColumnSchemaModel("TimeoutAt", dto, true, false),
            new ColumnSchemaModel("LastError", Str(2048), true, false),
            // NOT NULL (empty string in single-tenant hosts) — part of the active-saga unique key below.
            new ColumnSchemaModel("TenantId", Str(128), false, false),
            new ColumnSchemaModel("Version", i32, false, false));

        var indexes = ImmutableArray.Create(
            // TenantId in the key so two tenants can run the same (SagaType, CorrelationId) concurrently.
            new IndexSchemaModel("IX___SagaInstances_SagaType_CorrelationId_TenantId",
                ImmutableArray.Create("SagaType", "CorrelationId", "TenantId"), IsUnique: true, Filter: "\"Status\" = 0"),
            new IndexSchemaModel("IX___SagaInstances_Status_SagaType",
                ImmutableArray.Create("Status", "SagaType")),
            new IndexSchemaModel("IX___SagaInstances_Status_TimeoutAt",
                ImmutableArray.Create("Status", "TimeoutAt")));

        return new TableSchemaModel(SagaInstanceTableName, schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    /// <summary>
    ///     Emits the <c>__SagaSteps</c> table. Mirrors <c>SagaEntityTypeConfiguration.Configure(SagaStep)</c>,
    ///     including the cascade FK to <c>__SagaInstances</c>.
    /// </summary>
    private static TableSchemaModel BuildSagaStepsTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        var guid = SqlTypeMapper.MapToSqlType("System.Guid", provider);
        var i32 = SqlTypeMapper.MapToSqlType("System.Int32", provider);
        var dto = SqlTypeMapper.MapToSqlType("System.DateTimeOffset", provider);
        var strMax = SqlTypeMapper.MapToSqlType("System.String", provider);
        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("Id", guid, false, true),
            new ColumnSchemaModel("SagaInstanceId", guid, false, false),
            new ColumnSchemaModel("StepName", Str(256), false, false),
            new ColumnSchemaModel("Input", strMax, true, false),
            new ColumnSchemaModel("Output", strMax, true, false),
            new ColumnSchemaModel("Status", i32, false, false),
            new ColumnSchemaModel("StartedAt", dto, false, false),
            new ColumnSchemaModel("CompletedAt", dto, true, false),
            new ColumnSchemaModel("Error", Str(2048), true, false));

        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___SagaSteps_SagaInstanceId",
                ImmutableArray.Create("SagaInstanceId")));

        var foreignKeys = ImmutableArray.Create(
            new ForeignKeySchemaModel("FK___SagaSteps___SagaInstances_SagaInstanceId",
                "SagaInstanceId", SagaInstanceTableName, "Id", "Cascade"));

        return new TableSchemaModel(SagaStepTableName, schemaName, null, columns, indexes, foreignKeys);
    }

    private const string SagaInstanceTableName = "__SagaInstances";
    private const string SagaStepTableName = "__SagaSteps";

    /// <summary>
    ///     Emits the <c>__EventOutbox</c> table for an <c>[EnableEventOutbox]</c> database. Mirrors
    ///     <c>EventOutboxEntryConfiguration</c> so the migration schema and the EF model agree
    ///     (timestamp columns are stored as UTC ticks = <c>bigint</c>).
    /// </summary>
    private static TableSchemaModel BuildEventOutboxTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        var guid = SqlTypeMapper.MapToSqlType("System.Guid", provider);
        var ticks = SqlTypeMapper.MapToSqlType("System.Int64", provider);
        var count = SqlTypeMapper.MapToSqlType("System.Int32", provider);
        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);
        var strMax = SqlTypeMapper.MapToSqlType("System.String", provider);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("Id", guid, false, true),
            new ColumnSchemaModel("EventType", Str(512), false, false),
            new ColumnSchemaModel("Payload", strMax, false, false),
            new ColumnSchemaModel("OccurredAt", ticks, false, false),
            new ColumnSchemaModel("TenantId", Str(128), true, false),
            new ColumnSchemaModel("TraceParent", Str(64), true, false),
            new ColumnSchemaModel("CreatedAt", ticks, false, false),
            new ColumnSchemaModel("ProcessedAt", ticks, true, false),
            new ColumnSchemaModel("Attempts", count, false, false),
            new ColumnSchemaModel("LastError", strMax, true, false),
            new ColumnSchemaModel("ClaimedBy", Str(128), true, false),
            new ColumnSchemaModel("ClaimedUntil", ticks, true, false));

        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___EventOutbox_ProcessedAt_ClaimedUntil_CreatedAt",
                ImmutableArray.Create("ProcessedAt", "ClaimedUntil", "CreatedAt")));

        return new TableSchemaModel("__EventOutbox", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    /// <summary>
    ///     Emits the <c>__OutboxMessages</c> table for an <c>[EnableOutbox]</c> (Messaging
    ///     transport-publish outbox) database. Mirrors <c>OutboxEntityTypeConfiguration</c> so the
    ///     migration schema and the EF model agree. Timestamps are stored as UTC ticks
    ///     (<c>bigint</c>) — SQLite cannot translate ordering comparisons on <c>DateTimeOffset</c>,
    ///     so the config value-converts them to ticks (same choice as the event outbox).
    /// </summary>
    private static TableSchemaModel BuildOutboxMessagesTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        var guid = SqlTypeMapper.MapToSqlType("System.Guid", provider);
        var i32 = SqlTypeMapper.MapToSqlType("System.Int32", provider);
        var ticks = SqlTypeMapper.MapToSqlType("System.Int64", provider);
        var strMax = SqlTypeMapper.MapToSqlType("System.String", provider);
        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("Id", guid, false, true),
            new ColumnSchemaModel("MessageType", Str(512), false, false),
            new ColumnSchemaModel("Payload", strMax, false, false),
            new ColumnSchemaModel("CreatedAt", ticks, false, false),
            new ColumnSchemaModel("ProcessedAt", ticks, true, false),
            new ColumnSchemaModel("RetryCount", i32, false, false),
            new ColumnSchemaModel("NextAttemptAt", ticks, true, false),
            new ColumnSchemaModel("Error", Str(2048), true, false),
            new ColumnSchemaModel("ClaimedBy", Str(128), true, false),
            new ColumnSchemaModel("ClaimedUntil", ticks, true, false),
            new ColumnSchemaModel("CorrelationId", Str(64), true, false),
            new ColumnSchemaModel("TenantId", Str(128), true, false),
            new ColumnSchemaModel("UserId", Str(128), true, false),
            new ColumnSchemaModel("HeadersJson", strMax, true, false));

        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___OutboxMessages_ProcessedAt_NextAttemptAt_ClaimedUntil_CreatedAt",
                ImmutableArray.Create("ProcessedAt", "NextAttemptAt", "ClaimedUntil", "CreatedAt")),
            new IndexSchemaModel("IX___OutboxMessages_CorrelationId",
                ImmutableArray.Create("CorrelationId")));

        return new TableSchemaModel("__OutboxMessages", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    /// <summary>
    ///     Emits the <c>__BatchProgress</c> table for an <c>[EnableBatchProgress]</c> database. Mirrors
    ///     <c>BatchProgressEntityTypeConfiguration</c> so the migration schema and the EF model agree.
    ///     Timestamps stay <c>DateTimeOffset</c> (the store orders client-side, so no ticks conversion
    ///     is needed). <c>TenantId</c> is NOT NULL (empty string in single-tenant hosts).
    /// </summary>
    /// <summary>
    ///     Emits <c>__Jobs</c>. Mirrors <c>JobEntityTypeConfiguration.Configure</c> column for column and
    ///     index for index, so the migration and the EF model agree — a store whose schema disagrees with
    ///     its own configuration cannot lease a job.
    /// </summary>
    private static TableSchemaModel BuildJobsTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        var guid = SqlTypeMapper.MapToSqlType("System.Guid", provider);
        var i32 = SqlTypeMapper.MapToSqlType("System.Int32", provider);
        var dto = SqlTypeMapper.MapToSqlType("System.DateTimeOffset", provider);
        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("Id", guid, false, true),
            new ColumnSchemaModel("JobType", Str(512), false, false),
            // 64KB, the cap the configuration puts on the payload rather than an unbounded text column.
            new ColumnSchemaModel("ParametersJson", Str(65535), true, false),
            new ColumnSchemaModel("ParameterType", Str(512), true, false),
            new ColumnSchemaModel("Status", i32, false, false),
            new ColumnSchemaModel("ScheduledFor", dto, false, false),
            new ColumnSchemaModel("Priority", i32, false, false),
            new ColumnSchemaModel("StartedAt", dto, true, false),
            new ColumnSchemaModel("CompletedAt", dto, true, false),
            new ColumnSchemaModel("Error", Str(4096), true, false),
            new ColumnSchemaModel("Attempt", i32, false, false),
            new ColumnSchemaModel("MaxAttempts", i32, false, false),
            new ColumnSchemaModel("LeaseExpiresAt", dto, true, false),
            new ColumnSchemaModel("LeasedBy", Str(128), true, false),
            new ColumnSchemaModel("ContinuationJobType", Str(512), true, false),
            new ColumnSchemaModel("ContinuationParametersJson", SqlTypeMapper.MapToSqlType("System.String", provider), true, false),
            new ColumnSchemaModel("CorrelationId", Str(64), true, false),
            new ColumnSchemaModel("TenantId", Str(128), true, false),
            new ColumnSchemaModel("CreatedAt", dto, false, false));

        var indexes = ImmutableArray.Create(
            // The poll: filter on Status, order by priority then schedule.
            new IndexSchemaModel("IX___Jobs_Status_Priority_ScheduledFor",
                ImmutableArray.Create("Status", "Priority", "ScheduledFor")),
            // Lease cleanup.
            new IndexSchemaModel("IX___Jobs_Status_LeaseExpiresAt",
                ImmutableArray.Create("Status", "LeaseExpiresAt")),
            new IndexSchemaModel("IX___Jobs_CorrelationId",
                ImmutableArray.Create("CorrelationId")),
            // Retention purge: both indexes above lead with Status, so a cutoff on CompletedAt alone
            // would scan the whole history table.
            new IndexSchemaModel("IX___Jobs_CompletedAt",
                ImmutableArray.Create("CompletedAt")));

        return new TableSchemaModel("__Jobs", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    /// <summary>
    ///     Emits <c>__RecurringJobs</c>. Mirrors <c>RecurringJobEntityTypeConfiguration.Configure</c> —
    ///     the row an operator reads to know when a schedule runs next.
    /// </summary>
    private static TableSchemaModel BuildRecurringJobsTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        var i32 = SqlTypeMapper.MapToSqlType("System.Int32", provider);
        var dto = SqlTypeMapper.MapToSqlType("System.DateTimeOffset", provider);
        var boolean = SqlTypeMapper.MapToSqlType("System.Boolean", provider);
        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);

        var columns = ImmutableArray.Create(
            // The id is the schedule's name, not a surrogate: it is what [RecurringJob(Id = …)] says and
            // what an operator greps for.
            new ColumnSchemaModel("Id", Str(256), false, true),
            new ColumnSchemaModel("JobType", Str(512), false, false),
            new ColumnSchemaModel("CronExpression", Str(128), false, false),
            new ColumnSchemaModel("ParametersJson", Str(65535), true, false),
            new ColumnSchemaModel("ParameterType", Str(512), true, false),
            new ColumnSchemaModel("LastExecutedAt", dto, true, false),
            new ColumnSchemaModel("NextExecutionAt", dto, true, false),
            new ColumnSchemaModel("IsEnabled", boolean, false, false),
            new ColumnSchemaModel("TenantId", Str(128), true, false),
            new ColumnSchemaModel("TimeZoneId", Str(64), true, false),
            new ColumnSchemaModel("MisfirePolicy", i32, false, false));

        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___RecurringJobs_IsEnabled_NextExecutionAt",
                ImmutableArray.Create("IsEnabled", "NextExecutionAt")));

        return new TableSchemaModel("__RecurringJobs", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    private static TableSchemaModel BuildBatchProgressTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        var guid = SqlTypeMapper.MapToSqlType("System.Guid", provider);
        var i32 = SqlTypeMapper.MapToSqlType("System.Int32", provider);
        var dto = SqlTypeMapper.MapToSqlType("System.DateTimeOffset", provider);
        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("BatchId", guid, false, true),
            new ColumnSchemaModel("Total", i32, false, false),
            new ColumnSchemaModel("Completed", i32, false, false),
            new ColumnSchemaModel("Failed", i32, false, false),
            new ColumnSchemaModel("DispatchedCount", i32, false, false),
            new ColumnSchemaModel("StartedAt", dto, false, false),
            new ColumnSchemaModel("CompletedAt", dto, true, false),
            // NOT NULL (empty string in single-tenant hosts) — mirrors the tenant-isolated saga table.
            new ColumnSchemaModel("TenantId", Str(128), false, false),
            new ColumnSchemaModel("Label", Str(256), true, false));

        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___BatchProgress_CompletedAt",
                ImmutableArray.Create("CompletedAt")));

        return new TableSchemaModel("__BatchProgress", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    /// <summary>
    ///     <c>__SubjectKeys</c>, the per-subject keys that open this database's <c>ProtectedValue</c>
    ///     columns (dual-source with <c>SubjectKeyEntityTypeConfiguration</c>).
    /// </summary>
    /// <remarks>
    ///     ⚠️ In the same database as the ciphertext, and created by the same migration: erasing such a
    ///     column is destroying its key, and a key in another store would make that a distributed
    ///     transaction with a window in which the data is readable and the erasure says it is not.
    /// </remarks>
    private static TableSchemaModel BuildSubjectKeysTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        var dto = SqlTypeMapper.MapToSqlType("System.DateTimeOffset", provider);
        var bytes = SqlTypeMapper.MapToSqlType("byte[]", provider);
        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("SubjectRef", Str(128), false, true),
            new ColumnSchemaModel("KeyId", Str(64), false, false),
            new ColumnSchemaModel("WrappedKey", bytes, true, false),
            new ColumnSchemaModel("CreatedAt", dto, false, false),
            // Null until the key is destroyed; set is what makes every value under it unreadable.
            new ColumnSchemaModel("DestroyedAt", dto, true, false));

        // Unique on purpose: a reader holding only the ciphertext resolves by key id, and the id is a
        // fingerprint of the material — two rows sharing one means a key was reused.
        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___SubjectKeys_KeyId", ImmutableArray.Create("KeyId"), IsUnique: true));

        return new TableSchemaModel("__SubjectKeys", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    /// <summary>
    ///     Whether a navigation is the end that carries the key: a many-to-one, or the dependent end of
    ///     a one-to-one. Column, index and constraint all follow from this one answer.
    /// </summary>
    /// <summary>
    ///     Whether this entity is a derived type of a hierarchy whose root maps to one table (TPH).
    /// </summary>
    /// <remarks>
    ///     The strategy is declared on the root, so the answer is the root's to give: walk
    ///     <see cref="EntityMetadataModel.BaseEntityFullTypeName" /> up to the type that has none and
    ///     read its <see cref="EntityMetadataModel.InheritanceStrategy" />. A derived type carries no
    ///     strategy of its own, which is why asking it directly answers nothing.
    /// </remarks>
    private static bool IsTphDerived(
        EntityMetadataModel entity,
        ImmutableArray<EntityMetadataModel> all)
    {
        if (entity.BaseEntityFullTypeName is null)
            return false;

        var current = entity;
        // Bounded by the number of entities: a cycle is impossible in a C# base chain, and the bound
        // keeps a malformed model from spinning here.
        for (var hops = 0; hops < all.Length + 1; hops++)
        {
            var baseName = current.BaseEntityFullTypeName;
            if (baseName is null)
                return string.Equals(current.InheritanceStrategy, "TPH", StringComparison.Ordinal);

            var parent = all.FirstOrDefault(e => e.FullTypeName == baseName);
            if (parent is null)
                return false;

            current = parent;
        }

        return false;
    }

    private static bool CarriesForeignKey(NavigationMetadataModel nav)
        => !string.IsNullOrEmpty(nav.ForeignKeyProperty)
           && (nav.NavigationType == "ManyToOne"
               || (nav.NavigationType == "OneToOne" && !nav.IsPrincipal));

    private static List<TableSchemaModel> BuildManyToManyJoinTables(
        List<EntityMetadataModel> entities, EfCoreProvider provider)
    {
        var joinTables = new List<TableSchemaModel>();

        // Both ends of a many-to-many now resolve to the same join table, so both ends offer to build
        // it and only one may. Keeping the first one seen would hand the table's column order — its
        // primary key — to whichever entity the loop happened to reach first. The pair is ordered by
        // name instead, so the schema is the same however the entities arrive.
        var contributors = new Dictionary<string, (EntityMetadataModel Entity, NavigationMetadataModel Nav, string Right)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            foreach (var nav in entity.Navigations)
            {
                if (nav.NavigationType != "ManyToMany") continue;
                if (!string.IsNullOrEmpty(nav.JoinEntityTypeName)) continue; // Explicit join entity — already has its own table

                var right = nav.TargetTypeName.Contains('.')
                    ? nav.TargetTypeName.Substring(nav.TargetTypeName.LastIndexOf('.') + 1)
                    : nav.TargetTypeName;

                // ⚠️ Not `if (name is empty) continue`. That skipped the table whenever the author had
                // not named one, while the configuration left EF Core to its own convention — so EF
                // queried a table the migration had never created. The name comes from one place now.
                var name = JoinTableNaming.For(entity.TypeName, right, nav.JoinTable);

                if (!contributors.TryGetValue(name, out var chosen)
                    || string.CompareOrdinal(entity.TypeName, chosen.Entity.TypeName) < 0)
                {
                    contributors[name] = (entity, nav, right);
                }
            }
        }

        {
            // KeyValuePair does not deconstruct on netstandard2.0, which is what this generator targets.
            foreach (var pair in contributors.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var joinTableName = pair.Key;
                var entity = pair.Value.Entity;
                var nav = pair.Value.Nav;
                var rightSimple = pair.Value.Right;

                // Shared with the entity configuration, which tells EF Core what to look for. Two
                // namings for one table meant the migration created columns the model never asked
                // about — and on a self-reference the same name twice, which the database refuses.
                var (leftFk, rightFk) = JoinColumnNaming.For(
                    entity.TypeName, rightSimple, nav.Name, nav.InverseProperty);

                var pkType = SqlTypeMapper.MapToSqlType(entity.IdType ?? "System.Guid", provider);
                var schemaName = provider switch
                {
                    EfCoreProvider.PostgreSql => "public",
                    EfCoreProvider.SqlServer => "dbo",
                    _ => null
                };

                var columns = ImmutableArray.Create(
                    new ColumnSchemaModel(leftFk, pkType, false, true),
                    new ColumnSchemaModel(rightFk, pkType, false, true));

                var leftTable = StringHelper.Pluralize(entity.TypeName);
                var rightTable = StringHelper.Pluralize(rightSimple);

                var indexes = ImmutableArray.Create(
                    new IndexSchemaModel($"IX_{joinTableName}_{rightFk}", ImmutableArray.Create(rightFk)));

                var foreignKeys = ImmutableArray.Create(
                    new ForeignKeySchemaModel($"FK_{joinTableName}_{leftFk}_{leftTable}", leftFk, leftTable, "PersistenceId", "Cascade"),
                    new ForeignKeySchemaModel($"FK_{joinTableName}_{rightFk}_{rightTable}", rightFk, rightTable, "PersistenceId", "Cascade"));

                joinTables.Add(new TableSchemaModel(joinTableName, schemaName, null, columns, indexes, foreignKeys));
            }
        }

        return joinTables;
    }

    /// <summary>
    ///     Emits the audit trail's tables when any entity in the database is <c>[Audited]</c>.
    /// </summary>
    /// <remarks>
    ///     Mirrors <c>Pragmatic.Audit.EFCore</c>'s entity configurations so the migration schema and the
    ///     EF model agree. That duplication is the dual-source hazard this whole transform lives with:
    ///     the schema is generated from metadata while the model comes from EF, and nothing but care
    ///     keeps them aligned. The segments table has to be here too — an entry without its segment
    ///     cannot be sealed, and therefore cannot be verified.
    /// </remarks>
    private static TableSchemaModel? BuildAuditLogTable(List<EntityMetadataModel> entities, EfCoreProvider provider)
    {
        if (!entities.Any(e => e.IsAudited))
            return null;

        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);

        var ticks = SqlTypeMapper.MapToSqlType("System.Int64", provider);
        var bytes = SqlTypeMapper.MapToSqlType("System.Byte[]", provider);

        // Timestamps are ticks, not DateTimeOffset: the trail is read by more than one provider and
        // SQLite cannot translate a DateTimeOffset comparison at all.
        var columns = ImmutableArray.Create(
            // Seq is database-generated, and the schema model has no notion of an identity column — so
            // the provider-specific type carries it. A plain bigint here compiles and migrates fine and
            // then fails on the first insert, because EF expects the database to supply the value.
            new ColumnSchemaModel("Seq", provider switch
            {
                EfCoreProvider.PostgreSql => "BIGSERIAL",
                EfCoreProvider.SqlServer => "BIGINT IDENTITY(1,1)",
                _ => "INTEGER"
            }, false, true),
            new ColumnSchemaModel("SegmentId", Str(32), false, false),
            new ColumnSchemaModel("OccurredAt", ticks, false, false),
            new ColumnSchemaModel("Category", SqlTypeMapper.MapToSqlType("System.Int32", provider), false, false),
            new ColumnSchemaModel("Operation", Str(128), false, false),
            new ColumnSchemaModel("ActorRef", Str(128), true, false),
            new ColumnSchemaModel("SubjectRef", Str(128), true, false),
            new ColumnSchemaModel("TenantId", Str(128), true, false),
            new ColumnSchemaModel("CorrelationId", Str(64), true, false),
            new ColumnSchemaModel("BusinessOperation", Str(256), true, false),
            new ColumnSchemaModel("OnBehalfOfRef", Str(128), true, false),
            new ColumnSchemaModel("TargetType", Str(256), true, false),
            new ColumnSchemaModel("TargetId", Str(128), true, false),
            new ColumnSchemaModel("Outcome", SqlTypeMapper.MapToSqlType("System.Int32", provider), false, false),
            new ColumnSchemaModel("ValueHash", bytes, true, false),
            new ColumnSchemaModel("Detail", Str(2048), true, false));

        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___AuditEntries_SegmentId_Seq", ImmutableArray.Create("SegmentId", "Seq")),
            new IndexSchemaModel("IX___AuditEntries_OccurredAt", ImmutableArray.Create("OccurredAt")));

        return new TableSchemaModel("__AuditEntries", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    /// <summary>
    ///     The trail's segment table, emitted alongside its entries.
    /// </summary>
    /// <remarks>
    ///     Sealing works on whole segments and verification walks them in order, so a database that has
    ///     the entries without the segments holds a trail that can be written and never checked.
    /// </remarks>
    /// <summary>
    ///     The gaps retention left behind, and the hash the next segment still links to.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>AuditDbContext</c> has three <c>DbSet</c>s and the schema has to emit all three.
    ///     Without this one a host that turns the trail on gets two of its three tables, and the first
    ///     attempt to seal a segment fails with "relation __AuditPrunedRanges does not exist" — nothing
    ///     says so at build time. <c>AuditSchemaMatchesEfModelTests</c> keeps the schema and the EF
    ///     model in step for all three.
    /// </remarks>
    private static TableSchemaModel BuildAuditPrunedRangesTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);
        var ticks = SqlTypeMapper.MapToSqlType("System.Int64", provider);
        var bytes = SqlTypeMapper.MapToSqlType("System.Byte[]", provider);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("FromSegmentId", Str(32), false, true),
            new ColumnSchemaModel("UntilSegmentId", Str(32), false, false),
            new ColumnSchemaModel("PrunedAt", ticks, false, false),
            new ColumnSchemaModel("LinkHash", bytes, false, false));

        return new TableSchemaModel("__AuditPrunedRanges", schemaName, null, columns,
            ImmutableArray<IndexSchemaModel>.Empty,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    private static TableSchemaModel BuildAuditSegmentsTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);
        var ticks = SqlTypeMapper.MapToSqlType("System.Int64", provider);
        var bytes = SqlTypeMapper.MapToSqlType("System.Byte[]", provider);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("SegmentId", Str(32), false, true),
            new ColumnSchemaModel("OpenedAt", ticks, false, false),
            new ColumnSchemaModel("SealedAt", ticks, true, false),
            new ColumnSchemaModel("EntryCount", SqlTypeMapper.MapToSqlType("System.Int32", provider), false, false),
            new ColumnSchemaModel("MerkleRoot", bytes, true, false),
            new ColumnSchemaModel("PreviousHash", bytes, true, false),
            new ColumnSchemaModel("SegmentHash", bytes, true, false));

        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___AuditSegments_SealedAt", ImmutableArray.Create("SealedAt")));

        return new TableSchemaModel("__AuditSegments", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    private static TableSchemaModel TransformEntity(EntityMetadataModel entity, EfCoreProvider provider, SchemaResolutionContext ctx)
    {
        // Use custom table name from [Table("name")] if present, otherwise Pluralize(TypeName)
        var tableName = entity.CustomTableName ?? StringHelper.Pluralize(entity.TypeName);
        var schemaName = entity.CustomSchemaName ?? provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        var columns = BuildColumns(entity, provider, ctx);

        // TPH: merge derived entity columns into the base table (resolved from Compilation)
        if (entity.InheritanceStrategy == "TPH" && ctx.TphDerivedTypes.TryGetValue(entity.FullTypeName, out var derivedInfos))
            AddTphDerivedColumns(derivedInfos, provider, columns);

        var indexes = BuildIndexes(entity, tableName);
        var foreignKeys = BuildForeignKeys(entity, tableName);
        var checks = BuildCheckConstraints(entity, tableName);

        return new TableSchemaModel(tableName, schemaName, entity.FullTypeName,
            columns.ToImmutableArray(), indexes, foreignKeys, checks);
    }

    private static List<ColumnSchemaModel> BuildColumns(EntityMetadataModel entity, EfCoreProvider provider, SchemaResolutionContext ctx)
    {
        var columns = new List<ColumnSchemaModel>();

        // An entity keyed on its own columns (a junction on its two FKs) declares them explicitly;
        // every other entity gets the surrogate PersistenceId PK.
        var declaredKey = entity.KeyColumns.AsImmutableArray();
        var keyColumnNames = declaredKey.IsDefaultOrEmpty
            ? null
            : new HashSet<string>(declaredKey, StringComparer.OrdinalIgnoreCase);

        if (keyColumnNames is null)
            columns.Add(new ColumnSchemaModel(
                "PersistenceId",
                SqlTypeMapper.MapToSqlType(entity.IdType ?? "System.Guid", provider),
                IsNullable: false,
                IsPrimaryKey: true));

        // Explicit properties (excluding navigations)
        foreach (var prop in entity.Properties)
        {
            if (prop.IsNavigation) continue;
            if (prop.Name == "PersistenceId") continue; // Already added

            // [ValueObject] property → flatten into {Name}_{Sub} columns matching the EF ComplexProperty
            // mapping. Without this the value object would map to a single fallback column and migrations
            // would diverge from the EF model (the latent Money complex-type gap, generalized and closed).
            if (!prop.ValueObjectColumns.IsDefaultOrEmpty)
            {
                foreach (var col in prop.ValueObjectColumns)
                    columns.Add(new ColumnSchemaModel(
                        col.ColumnName,
                        SqlTypeMapper.MapToSqlType(col.TypeName, provider, null, null, null, col.IsEnum),
                        col.IsNullable,
                        IsPrimaryKey: false));
                continue;
            }

            var isKeyColumn = keyColumnNames?.Contains(prop.Name) == true;

            columns.Add(new ColumnSchemaModel(
                prop.Name,
                SqlTypeMapper.MapToSqlType(prop.TypeName, provider, prop.MaxLength, prop.Precision, prop.Scale, prop.IsEnum),
                // A key column is never nullable, whatever the CLR annotation says.
                isKeyColumn ? false : prop.IsNullable,
                IsPrimaryKey: isKeyColumn,
                // ⚠️ Translated, not forwarded. DefaultValueExpression is a C# literal — it is what the
                // generated factory assigns — and this column's default is interpolated raw into DDL.
                // Handing the C# form straight through would make `[DefaultValue("pending")]` emit
                // DEFAULT "pending", which PostgreSQL reads as a column reference and refuses: the
                // migration would fail and the application would not start.
                SqlDefaultMapper.ToSqlLiteral(prop.DefaultValueExpression, provider),
                prop.RenamedFrom));
        }

        // Trait-generated columns
        AddTraitColumns(entity, provider, columns);

        // Implicit FK columns — navigations with ForeignKeyProperty not already in explicit properties
        AddImplicitFkColumns(entity, provider, columns);

        // Owned entity columns — flattened into parent table with {Nav}_{Prop} naming
        AddOwnedEntityColumns(entity, provider, columns, ctx);

        return columns;
    }

    private static void AddTraitColumns(EntityMetadataModel entity, EfCoreProvider provider, List<ColumnSchemaModel> columns)
    {
        var has = new HashSet<string>(columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        // TPH discriminator column (EF Core auto-adds a discriminator for Table-Per-Hierarchy)
        if (!string.IsNullOrEmpty(entity.InheritanceStrategy) && entity.InheritanceStrategy == "TPH")
        {
            var discriminatorName = entity.DiscriminatorColumn ?? "Discriminator";
            TryAdd(discriminatorName, "System.String", false, null);
        }

        if (entity.IsAuditable)
        {
            TryAdd("CreatedAt", "System.DateTimeOffset", false, null);
            TryAdd("CreatedBy", "System.String", true, null);
            TryAdd("UpdatedAt", "System.DateTimeOffset?", true, null);
            TryAdd("UpdatedBy", "System.String", true, null);
        }

        if (entity.IsSoftDelete)
        {
            TryAdd("IsDeleted", "System.Boolean", false, "false");
            TryAdd("DeletedAt", "System.DateTimeOffset?", true, null);
            TryAdd("DeletedBy", "System.String", true, null);
        }

        if (entity.IsOwnedEntity)
            TryAdd("OwnerId", "System.String", false, null);

        if (entity.IsScopedEntity)
            TryAdd("AccessScopes", "List<string>", true, null);

        if (entity.IsTenantEntity)
            TryAdd("TenantId", "System.String", false, null);

        if (entity.IsTemporalRelation)
        {
            TryAdd("ValidFrom", "System.DateTimeOffset", false, null);
            TryAdd("ValidTo", "System.DateTimeOffset?", true, null);
        }

        return;

        void TryAdd(string name, string clrType, bool nullable, string? defaultValue)
        {
            if (has.Contains(name)) return;
            columns.Add(new ColumnSchemaModel(name,
                SqlTypeMapper.MapToSqlType(clrType, provider),
                nullable, false, defaultValue));
            has.Add(name);
        }
    }

    /// <summary>
    ///     Adds FK columns that are implied by ManyToOne navigations but not explicitly declared
    ///     as properties on the entity (EF Core shadow FK properties).
    /// </summary>
    private static void AddImplicitFkColumns(EntityMetadataModel entity, EfCoreProvider provider, List<ColumnSchemaModel> columns)
    {
        var existingNames = new HashSet<string>(columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var nav in entity.Navigations)
        {
            // A dependent one-to-one carries a key exactly like a many-to-one. Counting only the
            // latter, the key a [Relation.OneToOne] generates on the dependent would never reach the
            // schema — no column, no index, no constraint.
            if (!CarriesForeignKey(nav))
                continue;

            if (existingNames.Contains(nav.ForeignKeyProperty!))
                continue;

            // FK column type matches the PK type of the target entity (typically Guid)
            var fkType = SqlTypeMapper.MapToSqlType("System.Guid", provider);
            var isNullable = !nav.IsRequired;

            columns.Add(new ColumnSchemaModel(nav.ForeignKeyProperty!, fkType, isNullable, false));
            existingNames.Add(nav.ForeignKeyProperty!);
        }
    }

    /// <summary>
    ///     Merges TPH derived entity columns into the base table.
    ///     Derived-only columns are always nullable (not all rows belong to every derived type).
    /// </summary>
    private static void AddTphDerivedColumns(
        List<TphDerivedInfo> derivedInfos, EfCoreProvider provider, List<ColumnSchemaModel> columns)
    {
        var existingNames = new HashSet<string>(columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var derived in derivedInfos)
        {
            foreach (var (name, typeName, _, isEnum) in derived.Properties)
            {
                if (existingNames.Contains(name)) continue;

                // Derived-only columns must be nullable (not all rows belong to this derived type)
                columns.Add(new ColumnSchemaModel(
                    name,
                    SqlTypeMapper.MapToSqlType(typeName, provider, isEnum: isEnum),
                    IsNullable: true,
                    IsPrimaryKey: false));
                existingNames.Add(name);
            }
        }
    }

    /// <summary>
    ///     Flattens owned entity properties into the parent table.
    ///     EF Core OwnsOne maps owned properties with {NavigationName}_{PropertyName} column naming.
    ///     Supports nested owned types: Address.Country.Name → Address_Country_Name.
    /// </summary>
    private static void AddOwnedEntityColumns(EntityMetadataModel entity, EfCoreProvider provider, List<ColumnSchemaModel> columns, SchemaResolutionContext ctx)
    {
        var existingNames = new HashSet<string>(columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var nav in entity.Navigations)
        {
            if (!nav.IsOwned) continue;

            var targetSimple = nav.TargetTypeName.Contains('.')
                ? nav.TargetTypeName.Substring(nav.TargetTypeName.LastIndexOf('.') + 1)
                : nav.TargetTypeName;

            FlattenOwnedType(nav.Name + "_", targetSimple, provider, columns, existingNames, ctx,
                optional: !nav.IsRequired, depth: 0);
        }
    }

    // `optional`: the owned type hangs off a nullable navigation. EF maps it as an optional dependent and
    // writes NULL into every one of its columns when the navigation is null, so each column is nullable
    // whatever the owned type's own property says. Taking only the latter created Identity_PasswordHash
    // NOT NULL, and removing an account could not be saved.
    private static void FlattenOwnedType(
        string prefix, string ownedTypeName, EfCoreProvider provider,
        List<ColumnSchemaModel> columns, HashSet<string> existingNames,
        SchemaResolutionContext ctx, bool optional, int depth)
    {
        // Guard against infinite recursion (max 5 levels deep)
        if (depth > 5) return;

        if (!ctx.OwnedTypeProperties.TryGetValue(ownedTypeName, out var props))
            return;

        foreach (var (propName, propType, isNullable, isEnum) in props)
        {
            // Check if this property is itself an owned type (nested)
            var propTypeSimple = propType.Contains('.')
                ? propType.Substring(propType.LastIndexOf('.') + 1)
                : propType;
            // Strip nullable annotation
            if (propTypeSimple.EndsWith("?", StringComparison.Ordinal))
                propTypeSimple = propTypeSimple.TrimEnd('?');

            if (ctx.OwnedTypeProperties.ContainsKey(propTypeSimple))
            {
                // Recurse: this property is a nested owned type, optional if it or its owner is
                FlattenOwnedType(prefix + propName + "_", propTypeSimple, provider,
                    columns, existingNames, ctx, optional || isNullable, depth + 1);
                continue;
            }

            var colName = prefix + propName;
            if (existingNames.Contains(colName)) continue;

            columns.Add(new ColumnSchemaModel(
                colName,
                SqlTypeMapper.MapToSqlType(propType, provider, isEnum: isEnum),
                isNullable || optional,
                false));
            existingNames.Add(colName);
        }
    }

    /// <summary>
    ///     Row-level invariants: currently, that a temporal stretch does not end before it begins.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A backdated handover can try to write one. <c>AutoClosePrevious</c> closes
    ///         everything whose <c>ValidTo</c> is null or later than the instant being written — which,
    ///         for an instant in the past, includes stretches that <em>begin</em> after it. Those would
    ///         get a <c>ValidTo</c> earlier than their own <c>ValidFrom</c>, and an inverted stretch matches no
    ///         <c>ActiveAt</c> window at all: the person who genuinely held the relation during those
    ///         weeks disappears from the history, silently.
    ///     </para>
    ///     <para>
    ///         A check rather than an index, because this constrains one row rather than the relationship
    ///         between rows — and, unlike the partial unique index beside it, it is evaluated per row as
    ///         that row is written, so a close-then-open inside one SaveChanges does not trip it.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<CheckConstraintSchemaModel> BuildCheckConstraints(
        EntityMetadataModel entity, string tableName)
    {
        if (!entity.IsTemporalRelation)
            return ImmutableArray<CheckConstraintSchemaModel>.Empty;

        return ImmutableArray.Create(new CheckConstraintSchemaModel(
            $"CK_{tableName}_ValidRange",
            "\"ValidTo\" IS NULL OR \"ValidTo\" >= \"ValidFrom\""));
    }

    private static ImmutableArray<IndexSchemaModel> BuildIndexes(EntityMetadataModel entity, string tableName)
    {
        var indexes = new List<IndexSchemaModel>();

        // The domain key's unique index. Read from LogicKeyColumns rather than assembled here: it
        // carries the order the parts asked for AND the leading TenantId a per-tenant key needs, and
        // this index has to be byte-for-byte the one EntityConfigurationTemplate emits. Assembling it
        // twice is how the model and the database come to disagree about what is unique.
        var logicKeyColumns = entity.LogicKeyColumns;

        if (logicKeyColumns.Length > 0)
        {
            var filter = entity.IsSoftDelete ? "\"IsDeleted\" = false" : null;
            indexes.Add(new IndexSchemaModel(
                $"IX_{tableName}_{string.Join("_", logicKeyColumns)}",
                logicKeyColumns,
                IsUnique: true,
                Filter: filter));
        }

        // MaxActive = 1 scoped to a parent: one open stretch per parent, which is exactly a partial
        // unique index.
        //
        // ⚠️ It has to be here and not only in the DbContext's OnModelCreating. The generated schema is
        // what the migrations build the database from, and OnModelCreating only describes the model EF
        // holds in memory: declared there alone, the constraint exists for EF and not for the database,
        // and a write that bypasses EF — or any write at all, since the column has no such index —
        // still succeeds, including a second open stretch written through the repository.
        if (entity is { IsTemporalRelation: true, TemporalMaxActive: 1, TemporalParentFkProperty: not null })
        {
            var parentKey = entity.TemporalParentFkProperty;
            indexes.Add(new IndexSchemaModel(
                $"IX_{tableName}_{parentKey}_Active",
                ImmutableArray.Create(parentKey),
                IsUnique: true,
                Filter: "\"ValidTo\" IS NULL"));
        }

        // [Unique] indexes. Emitted here as well as in the entity configuration because the
        // migrations build the database from this schema and OnModelCreating only describes the model
        // EF holds in memory: declared there alone, the constraint exists for EF and not for the
        // database, and any write that bypasses EF — or any write at all — still succeeds.
        foreach (var index in entity.UniqueIndexes)
        {
            var cols = entity.UniqueIndexColumns(index);
            indexes.Add(new IndexSchemaModel(
                $"IX_{tableName}_{string.Join("_", cols)}",
                cols,
                IsUnique: true,
                Filter: entity.IsSoftDelete ? "\"IsDeleted\" = false" : null));
        }

        // Composite unique constraint (the single-column case is [LogicKey] above)
        if (entity.UniqueColumns.Length > 0)
        {
            var cols = entity.UniqueColumns.AsImmutableArray();
            indexes.Add(new IndexSchemaModel(
                $"IX_{tableName}_{string.Join("_", cols)}",
                cols,
                IsUnique: true,
                Filter: entity.IsSoftDelete ? "\"IsDeleted\" = false" : null));
        }

        // Soft-delete index
        if (entity.IsSoftDelete)
            indexes.Add(new IndexSchemaModel($"IX_{tableName}_IsDeleted",
                ImmutableArray.Create("IsDeleted")));

        // Ownership index
        if (entity.IsOwnedEntity)
            indexes.Add(new IndexSchemaModel($"IX_{tableName}_OwnerId",
                ImmutableArray.Create("OwnerId")));

        // Tenant index
        if (entity.IsTenantEntity)
            indexes.Add(new IndexSchemaModel($"IX_{tableName}_TenantId",
                ImmutableArray.Create("TenantId")));

        // FK indexes (for ManyToOne navigations)
        foreach (var nav in entity.Navigations)
        {
            // A dependent one-to-one carries a key exactly like a many-to-one. Counting only the
            // latter, the key a [Relation.OneToOne] generates on the dependent would never reach the
            // schema — no column, no index, no constraint.
            if (!CarriesForeignKey(nav))
                continue;

            var fkIndexName = $"IX_{tableName}_{nav.ForeignKeyProperty}";
            if (indexes.Any(i => i.Name == fkIndexName)) continue;

            // ⚠️ One-to-one means one: the dependent's key must be unique in the database, not only in
            // the model EF holds. `HasOne().WithOne().HasForeignKey<TDependent>` makes EF enforce it
            // and lets EF's own convention build a unique index — but this schema is what
            // Pragmatic.Migrations creates the table from, so it must say unique itself: with the
            // plain index a many-to-one gets, a second dependent row pointing at the same principal
            // would be accepted by the database. The filter follows the [Unique] block above for the same reason: a
            // dependent that is soft-deletable must not have its marked row block the next one.
            var isOneToOne = nav.NavigationType == "OneToOne";

            indexes.Add(new IndexSchemaModel(fkIndexName,
                ImmutableArray.Create(nav.ForeignKeyProperty!),
                IsUnique: isOneToOne,
                Filter: isOneToOne && entity.IsSoftDelete ? "\"IsDeleted\" = false" : null));
        }

        return indexes.ToImmutableArray();
    }

    private static ImmutableArray<ForeignKeySchemaModel> BuildForeignKeys(EntityMetadataModel entity, string tableName)
    {
        var fks = new List<ForeignKeySchemaModel>();

        foreach (var nav in entity.Navigations)
        {
            // A dependent one-to-one carries a key exactly like a many-to-one. Counting only the
            // latter, the key a [Relation.OneToOne] generates on the dependent would never reach the
            // schema — no column, no index, no constraint.
            if (!CarriesForeignKey(nav))
                continue;

            // Skip cross-boundary FK: the referenced table is another boundary's, excluded from this
            // boundary's migrations, so a constraint here would name a table this schema does not
            // create. That holds for a [ReadAccess<T>] crossing too — readable is not owned.
            if (nav.IsCrossBoundary(entity.BoundaryTypeFullName))
                continue;

            var targetSimpleName = nav.TargetTypeName.Contains('.')
                ? nav.TargetTypeName.Substring(nav.TargetTypeName.LastIndexOf('.') + 1)
                : nav.TargetTypeName;
            var referencedTable = StringHelper.Pluralize(targetSimpleName);
            var fkName = $"FK_{tableName}_{nav.ForeignKeyProperty}_{referencedTable}";
            if (fks.Any(f => f.Name == fkName)) continue;

            fks.Add(new ForeignKeySchemaModel(
                fkName,
                nav.ForeignKeyProperty!,
                referencedTable,
                "PersistenceId",
                nav.OnDelete));
        }

        return fks.ToImmutableArray();
    }
}
