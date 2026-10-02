namespace TimeOff.Leave.Dtos;

/// <summary>
///     A leave request with everything about it in one answer: who asked, the kind, the period and what
///     it takes, and the decision — read in a single SQL query.
/// </summary>
[MapFrom<LeaveRequest>]
[GenerateProjection]
public partial class LeaveRequestDetailDto
{
    public Guid Id { get; init; }

    public DateOnly From { get; init; }

    public DateOnly To { get; init; }

    public decimal? Hours { get; init; }

    /// <summary>What the request takes from the allowance, in the kind's unit.</summary>
    public decimal Amount { get; init; }

    public LeaveRequestStatus Status { get; init; }

    // ⚠️ No Reason here either, and for the same reason as in LeaveRequestDto: it is crypto-shredded,
    // so it is read through ISubjectDataProtector with its outcome, not projected. "Everything about it
    // in one answer" stops where a three-outcome read begins.

    /// <summary>Who asked.</summary>
    public EmployeeReferenceDto Employee { get; init; } = null!;

    /// <summary>The kind of absence, named in the language of the request.</summary>
    public AbsenceKindReferenceDto AbsenceKind { get; init; } = null!;

    /// <summary>The decision: by whom, when, and why — null while the request is pending.</summary>
    /// <remarks>
    ///     Built from the request's own columns (<see cref="LeaveDecisionDetailDto" /> maps the same row),
    ///     and only once someone decided: the condition is part of the SQL projection, not only of the
    ///     in-memory mapping.
    /// </remarks>
    [MapCondition(nameof(IsDecided))]
    public LeaveDecisionDetailDto? Decision { get; init; }

    private static bool IsDecided(LeaveRequest request) => request.Status != LeaveRequestStatus.Pending;
}
