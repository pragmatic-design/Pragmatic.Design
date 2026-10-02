namespace Pragmatic.Audit.AdoNet;

/// <summary>SQL Server statements for the ADO.NET audit writer.</summary>
public sealed class SqlServerAuditDialect : IAuditSqlDialect
{
    /// <inheritdoc />
    public string InsertEntry =>
        """
        INSERT INTO [__AuditEntries]
            ([SegmentId],[OccurredAt],[Category],[Operation],[ActorRef],[SubjectRef],[TenantId],
             [CorrelationId],[BusinessOperation],[OnBehalfOfRef],[TargetType],[TargetId],[Outcome],[ValueHash],[Detail])
        VALUES (@segmentId,@occurredAt,@category,@operation,@actorRef,@subjectRef,@tenantId,
                @correlationId,@businessOperation,@onBehalfOfRef,@targetType,@targetId,@outcome,@valueHash,@detail)
        """;

    /// <summary>
    ///     Inserts only when absent.
    /// </summary>
    /// <remarks>
    ///     Written as a guarded INSERT rather than MERGE: MERGE on SQL Server has a documented history
    ///     of concurrency defects, and the primary key makes a losing race a duplicate-key error rather
    ///     than a wrong row — which is why it is caught and ignored by the writer.
    /// </remarks>
    public string UpsertSegment =>
        """
        IF NOT EXISTS (SELECT 1 FROM [__AuditSegments] WHERE [SegmentId] = @segmentId)
            INSERT INTO [__AuditSegments] ([SegmentId],[OpenedAt],[EntryCount])
            VALUES (@segmentId,@openedAt,0)
        """;

    /// <inheritdoc />
    public string SelectSegmentSealed =>
        "SELECT [SealedAt] FROM [__AuditSegments] WHERE [SegmentId] = @segmentId";

    /// <inheritdoc />
    /// <remarks>
    ///     SQL Server has no CREATE TABLE IF NOT EXISTS, so each statement is guarded by its own
    ///     existence check rather than relying on the batch failing harmlessly.
    /// </remarks>
    public string CreateSchema =>
        """
        IF OBJECT_ID(N'[__AuditSegments]', N'U') IS NULL
        CREATE TABLE [__AuditSegments] (
            [SegmentId] VARCHAR(32) PRIMARY KEY,
            [OpenedAt] BIGINT NOT NULL,
            [SealedAt] BIGINT NULL,
            [EntryCount] INT NOT NULL,
            [MerkleRoot] VARBINARY(MAX) NULL,
            [PreviousHash] VARBINARY(MAX) NULL,
            [SegmentHash] VARBINARY(MAX) NULL
        );
        IF OBJECT_ID(N'[__AuditEntries]', N'U') IS NULL
        CREATE TABLE [__AuditEntries] (
            [Seq] BIGINT IDENTITY(1,1) PRIMARY KEY,
            [SegmentId] VARCHAR(32) NOT NULL,
            [OccurredAt] BIGINT NOT NULL,
            [Category] INT NOT NULL,
            [Operation] NVARCHAR(128) NOT NULL,
            [ActorRef] NVARCHAR(128) NULL,
            [SubjectRef] NVARCHAR(128) NULL,
            [TenantId] NVARCHAR(128) NULL,
            [CorrelationId] NVARCHAR(64) NULL,
            [BusinessOperation] NVARCHAR(256) NULL,
            [OnBehalfOfRef] NVARCHAR(128) NULL,
            [TargetType] NVARCHAR(256) NULL,
            [TargetId] NVARCHAR(128) NULL,
            [Outcome] INT NOT NULL,
            [ValueHash] VARBINARY(MAX) NULL,
            [Detail] NVARCHAR(2048) NULL
        );
        IF OBJECT_ID(N'[__PrunedRanges]', N'U') IS NULL
        CREATE TABLE [__PrunedRanges] (
            [FromSegmentId] VARCHAR(32) NOT NULL,
            [UntilSegmentId] VARCHAR(32) NOT NULL,
            [PrunedAt] BIGINT NOT NULL,
            [LinkHash] VARBINARY(MAX) NOT NULL,
            PRIMARY KEY ([FromSegmentId], [UntilSegmentId])
        );
        """;
}
