namespace Pragmatic.Audit.AdoNet;

/// <summary>PostgreSQL statements for the ADO.NET audit writer.</summary>
public sealed class PostgresAuditDialect : IAuditSqlDialect
{
    /// <inheritdoc />
    public string InsertEntry =>
        """
        INSERT INTO "__AuditEntries"
            ("SegmentId","OccurredAt","Category","Operation","ActorRef","SubjectRef","TenantId",
             "CorrelationId","BusinessOperation","OnBehalfOfRef","TargetType","TargetId","Outcome","ValueHash","Detail")
        VALUES (@segmentId,@occurredAt,@category,@operation,@actorRef,@subjectRef,@tenantId,
                @correlationId,@businessOperation,@onBehalfOfRef,@targetType,@targetId,@outcome,@valueHash,@detail)
        """;

    /// <inheritdoc />
    public string UpsertSegment =>
        """
        INSERT INTO "__AuditSegments" ("SegmentId","OpenedAt","EntryCount")
        VALUES (@segmentId,@openedAt,0)
        ON CONFLICT ("SegmentId") DO NOTHING
        """;

    /// <inheritdoc />
    public string SelectSegmentSealed =>
        """SELECT "SealedAt" FROM "__AuditSegments" WHERE "SegmentId" = @segmentId""";

    /// <inheritdoc />
    public string CreateSchema =>
        """
        CREATE TABLE IF NOT EXISTS "__AuditSegments" (
            "SegmentId" VARCHAR(32) PRIMARY KEY,
            "OpenedAt" BIGINT NOT NULL,
            "SealedAt" BIGINT NULL,
            "EntryCount" INTEGER NOT NULL,
            "MerkleRoot" BYTEA NULL,
            "PreviousHash" BYTEA NULL,
            "SegmentHash" BYTEA NULL
        );
        CREATE TABLE IF NOT EXISTS "__AuditEntries" (
            "Seq" BIGSERIAL PRIMARY KEY,
            "SegmentId" VARCHAR(32) NOT NULL,
            "OccurredAt" BIGINT NOT NULL,
            "Category" INTEGER NOT NULL,
            "Operation" VARCHAR(128) NOT NULL,
            "ActorRef" VARCHAR(128) NULL,
            "SubjectRef" VARCHAR(128) NULL,
            "TenantId" VARCHAR(128) NULL,
            "CorrelationId" VARCHAR(64) NULL,
            "BusinessOperation" VARCHAR(256) NULL,
            "OnBehalfOfRef" VARCHAR(128) NULL,
            "TargetType" VARCHAR(256) NULL,
            "TargetId" VARCHAR(128) NULL,
            "Outcome" INTEGER NOT NULL,
            "ValueHash" BYTEA NULL,
            "Detail" TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS "__PrunedRanges" (
            "FromSegmentId" VARCHAR(32) NOT NULL,
            "UntilSegmentId" VARCHAR(32) NOT NULL,
            "PrunedAt" BIGINT NOT NULL,
            "LinkHash" BYTEA NOT NULL,
            PRIMARY KEY ("FromSegmentId", "UntilSegmentId")
        );
        CREATE INDEX IF NOT EXISTS "IX___AuditEntries_SegmentId_Seq"
            ON "__AuditEntries" ("SegmentId", "Seq");
        CREATE INDEX IF NOT EXISTS "IX___AuditEntries_OccurredAt"
            ON "__AuditEntries" ("OccurredAt");
        """;
}
