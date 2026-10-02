using Pragmatic.Audit;

namespace TimeOff.Leave.Dtos;

/// <summary>
///     A decision on a leave request as the audit trail keeps it: what, when, and who by reference.
/// </summary>
[MapFrom<AuditEntry>]
public sealed partial record LeaveDecisionDto(
    string Operation,
    DateTimeOffset OccurredAt,
    [MapProperty(nameof(AuditEntry.ActorRef))] string? DecidedBy);
