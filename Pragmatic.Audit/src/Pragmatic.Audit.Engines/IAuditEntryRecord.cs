namespace Pragmatic.Audit.Engines;

/// <summary>An entry of the trail as both engines store it: <see cref="AuditEntry" />'s fields, its instant in UTC ticks.</summary>
internal interface IAuditEntryRecord
{
    long Seq { get; set; }

    string SegmentId { get; set; }

    long OccurredAt { get; set; }

    AuditCategory Category { get; set; }

    string Operation { get; set; }

    string? ActorRef { get; set; }

    string? SubjectRef { get; set; }

    string? TenantId { get; set; }

    string? CorrelationId { get; set; }

    string? BusinessOperation { get; set; }

    string? OnBehalfOfRef { get; set; }

    string? TargetType { get; set; }

    string? TargetId { get; set; }

    AuditOutcome Outcome { get; set; }

    byte[]? ValueHash { get; set; }

    string? Detail { get; set; }
}
