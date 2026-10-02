namespace TimeOff.Leave.Dtos;

/// <summary>A manager's decision on a request: who took it, when, and what they wrote.</summary>
/// <remarks>
///     It maps the request itself — the decision is three of its columns and a navigation — and a detail
///     nests it only once the request is decided, so what is nullable on a pending request is not here.
/// </remarks>
[MapFrom<LeaveRequest>]
public partial class LeaveDecisionDetailDto
{
    public EmployeeReferenceDto DecidedBy { get; init; } = null!;

    public DateTimeOffset DecidedAt { get; init; }

    [MapProperty(nameof(LeaveRequest.DecisionNote))]
    public string? Note { get; init; }
}
