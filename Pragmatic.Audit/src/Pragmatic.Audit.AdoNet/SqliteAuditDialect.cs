namespace Pragmatic.Audit.AdoNet;

/// <summary>SQLite statements for the ADO.NET audit writer.</summary>
public sealed class SqliteAuditDialect : IAuditSqlDialect
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
        INSERT OR IGNORE INTO "__AuditSegments" ("SegmentId","OpenedAt","EntryCount")
        VALUES (@segmentId,@openedAt,0)
        """;

    /// <inheritdoc />
    public string SelectSegmentSealed =>
        """SELECT "SealedAt" FROM "__AuditSegments" WHERE "SegmentId" = @segmentId""";

    /// <inheritdoc />
    public string CreateSchema =>
        """
        CREATE TABLE IF NOT EXISTS "__AuditSegments" (
            "SegmentId" VARCHAR(32) PRIMARY KEY,
            "OpenedAt" INTEGER NOT NULL,
            "SealedAt" INTEGER NULL,
            "EntryCount" INTEGER NOT NULL,
            "MerkleRoot" BLOB NULL,
            "PreviousHash" BLOB NULL,
            "SegmentHash" BLOB NULL
        );
        CREATE TABLE IF NOT EXISTS "__AuditEntries" (
            "Seq" INTEGER PRIMARY KEY AUTOINCREMENT,
            "SegmentId" VARCHAR(32) NOT NULL,
            "OccurredAt" INTEGER NOT NULL,
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
            "ValueHash" BLOB NULL,
            "Detail" TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS "__PrunedRanges" (
            "FromSegmentId" VARCHAR(32) NOT NULL,
            "UntilSegmentId" VARCHAR(32) NOT NULL,
            "PrunedAt" INTEGER NOT NULL,
            "LinkHash" BLOB NOT NULL,
            PRIMARY KEY ("FromSegmentId", "UntilSegmentId")
        );
        CREATE INDEX IF NOT EXISTS "IX___AuditEntries_SegmentId_Seq"
            ON "__AuditEntries" ("SegmentId", "Seq");
        CREATE INDEX IF NOT EXISTS "IX___AuditEntries_OccurredAt"
            ON "__AuditEntries" ("OccurredAt");
        """;
}
