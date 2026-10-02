namespace Pragmatic.Audit;

/// <summary>
///     Filters for reading the trail. All filters are optional and combine with AND.
/// </summary>
/// <remarks>
///     There is no filter on a person's identity, only on <see cref="SubjectRef" /> — the pseudonym. The
///     trail does not know who anyone is, and being unable to ask is what keeps that true.
/// </remarks>
public sealed record AuditQuery
{
    /// <summary>Only entries at or after this instant.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Only entries before this instant.</summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>Only entries in this category.</summary>
    public AuditCategory? Category { get; init; }

    /// <summary>Only entries about this subject pseudonym.</summary>
    public string? SubjectRef { get; init; }

    /// <summary>Only entries by this actor pseudonym.</summary>
    public string? ActorRef { get; init; }

    /// <summary>Only entries for this tenant.</summary>
    public string? TenantId { get; init; }

    /// <summary>Only entries sharing this correlation id.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Only entries with this outcome.</summary>
    public AuditOutcome? Outcome { get; init; }

    /// <summary>Only entries about this kind of thing, e.g. a message type or an entity name.</summary>
    public string? TargetType { get; init; }

    /// <summary>
    ///     Only entries about this one thing — its key, as <see cref="AuditEntry.TargetId" /> records it.
    ///     Usually with <see cref="TargetType" />: keys of different kinds of thing can coincide.
    /// </summary>
    public string? TargetId { get; init; }

    /// <summary>Maximum entries to return. Capped by the store.</summary>
    public int Limit { get; init; } = 100;

    /// <summary>Entries to skip.</summary>
    public int Offset { get; init; }
}
