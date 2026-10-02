namespace TimeOff.Leave.Dtos;

/// <summary>
///     A leave request: whose, of what kind, the period, what it takes, and where it stands.
/// </summary>
[MapFrom<LeaveRequest>]
[GenerateProjection]
public partial class LeaveRequestDto
{
    public Guid Id { get; init; }

    public Guid EmployeeId { get; init; }

    public Guid AbsenceKindId { get; init; }

    public DateOnly From { get; init; }

    public DateOnly To { get; init; }

    public decimal? Hours { get; init; }

    /// <summary>What the request takes from the allowance, in the kind's unit.</summary>
    public decimal Amount { get; init; }

    public LeaveRequestStatus Status { get; init; }

    // ⚠️ No Reason, and it is not an omission. It is stored encrypted under the employee's own key
    // (LeaveRequest.Reason), so reading it is asynchronous and has three outcomes — the text, no key,
    // and "the key was destroyed". A projection can express none of that, and one that returned an
    // empty string would say the employee gave no reason. GetLeaveRequestReasonQuery is the read.

    public string? DecisionNote { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }
}
