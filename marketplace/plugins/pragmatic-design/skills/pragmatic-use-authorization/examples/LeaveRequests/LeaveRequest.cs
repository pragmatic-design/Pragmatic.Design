using Pragmatic.Cryptography;

namespace TimeOff.Leave.Entities;

/// <summary>
///     An employee's request for time off: a period of whole days, or some hours of one day.
/// </summary>
/// <remarks>
///     <para>
///         What the request takes from the allowance is fixed when it is submitted: <see cref="Amount" />,
///         in the kind's unit — the working days of the period, or the hours. A holiday added later does
///         not change a request already made.
///     </para>
///     <para>
///         Visible to its requester and to the manager of the team it was asked in, and to whoever may
///         see every request (<c>view-all</c>): the scopes are stamped at submission (<see cref="TeamScopes" />).
///         A request stays with the team it was asked in, not with the employee — which is why it records
///         that team (<c>TeamId</c>): the absence report counts it there. The one exception is deliberate:
///         when HR transfers the employee, a request still pending follows them to the new manager
///         (<see cref="FollowToTeam" />). A team deleted afterwards leaves the request without one, and its
///         scopes as they were.
///     </para>
///     <para>
///         <c>[Audited]</c>: every change is in the audit trail, with who made it.
///     </para>
/// </remarks>
[Entity]
[Auditable]
[Audited]
[ConcurrencyAware]
[HasAccessScopes]
[StateMachine<LeaveRequestStatus>]
[Relation.ManyToOne<Employee>.WithNavigation("Employee")]
[Relation.ManyToOne<Employee>.WithNavigation("DecidedBy", Required = false)]
[Relation.ManyToOne<AbsenceKind>.WithNavigation("AbsenceKind")]
[Relation.ManyToOne<Allowance>.WithNavigation("Allowance", Required = false, Inverse = "Requests")]
[Relation.ManyToOne<Team>.WithNavigation("Team", Required = false, OnDelete = DeleteBehavior.SetNull)]
[LinksToSubject("Employee")]
public partial class LeaveRequest : DomainEventSource, IEntity
{
    public DateOnly From { get; private set; }

    public DateOnly To { get; private set; }

    /// <summary>For a kind counted in hours: how many, on the one day of the request.</summary>
    public decimal? Hours { get; private set; }

    /// <summary>What the request takes from the allowance, in the kind's unit.</summary>
    public decimal Amount { get; private set; }

    public LeaveRequestStatus Status { get; private set; } = LeaveRequestStatus.Pending;

    /// <summary>
    ///     Why the employee asks. Free text, and a reason for leave can say what is wrong with someone —
    ///     so it is stored encrypted under that employee's own key, and erased by destroying it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Special category data, and the only field in this application that is crypto-shredded.</b>
    ///         The others are cleared or anonymised in place; this one is not. Erasing it destroys the
    ///         key, which leaves the row and the ciphertext untouched and makes them unreadable —
    ///         including in every backup taken before the erasure, which is what clearing a column
    ///         cannot do.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>It is not readable as a property, and that is the point.</b> Reading it is
    ///         asynchronous and has three outcomes — the text, "no key" and "the key was destroyed" —
    ///         so it goes through <c>ISubjectDataProtector</c> and the caller handles the outcome. That
    ///         is why it is absent from every DTO: a projection cannot express an erased value, and one
    ///         that returned an empty string for it would say the employee gave no reason.
    ///         <c>GetLeaveRequestReasonQuery</c> is the read.
    ///     </para>
    ///     <para>
    ///         ⚠️ Storable because the generated entity configuration applies
    ///         <c>ProtectedValueConverter</c>. Without it, <c>ErasureStrategy.DestroyKey</c> would be
    ///         declarable, validated by <c>PRAG2902</c> and impossible to honour.
    ///     </para>
    /// </remarks>
    [PersonalData(DataCategory.Special, Erasure = ErasureStrategy.DestroyKey, Encrypted = true)]
    public ProtectedValue Reason { get; private set; }

    /// <summary>What the manager wrote with the decision — about the employee, to the employee.</summary>
    [MaxLength(500)]
    [PersonalData(DataCategory.Behavioural)]
    public string? DecisionNote { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>
    ///     The month the request starts in, which is the month it counts in: a request across two months
    ///     is reported in the first. Computed by the database wherever it is read.
    /// </summary>
    [Projectable]
    public int StartMonth => From.Month;

    /// <summary>
    ///     Opens a request: the period, what it takes and from which allowance — none for a kind that
    ///     uses no allowance, like sick leave — why, and who may see it. The caller has counted the
    ///     amount and checked it against the allowance and the other requests.
    /// </summary>
    internal static LeaveRequest Open(
        Guid employeeId, string requesterUserId, Guid? teamId,
        Guid absenceKindId, Guid? allowanceId, DateOnly from, DateOnly to, decimal? hours, decimal amount, ProtectedValue reason)
    {
        var request = Create();
        request.SetEmployeeId(employeeId);
        request.SetTeamId(teamId);
        request.SetAbsenceKindId(absenceKindId);
        request.SetAllowanceId(allowanceId);
        request.SetFrom(from);
        request.SetTo(to);
        request.SetHours(hours);
        request.SetAmount(amount);
        request.SetReason(reason);

        request.AccessScopes.Add(ScopeIdentifiers.ForUser(requesterUserId));
        if (teamId is { } team)
            request.AccessScopes.Add(TeamScopes.ScopeOf(team));

        return request;
    }

    /// <summary>
    ///     A request still waiting for a decision follows its employee to another team: it counts there
    ///     now, and the manager who sees it is that team's instead of the old one's. A decided request
    ///     stays where it was decided, and a request already in that team has nowhere to go: both answer
    ///     false and change nothing.
    /// </summary>
    internal bool FollowToTeam(Guid teamId)
    {
        if (Status != LeaveRequestStatus.Pending || TeamId == teamId)
            return false;

        if (TeamId is { } previous)
            RevokeScope(TeamScopes.ScopeOf(previous));
        SetTeamId(teamId);
        GrantScope(TeamScopes.ScopeOf(teamId));
        return true;
    }

    /// <summary>
    ///     A manager's decision: the transition, who took it, when, and why. The state machine refuses a
    ///     decision on a request that is not pending.
    /// </summary>
    internal Result<LeaveRequestStatus, IError> Decide(
        LeaveRequestStatus decision, Guid deciderId, string? note, DateTimeOffset at)
    {
        // Set before the transition: the event the transition raises reads them.
        SetDecidedById(deciderId);
        SetDecisionNote(note);
        SetDecidedAt(at);

        var moved = TransitionTo(decision);
        return moved.IsFailure
            ? Result<LeaveRequestStatus, IError>.Failure(moved.Error)
            : Result<LeaveRequestStatus, IError>.Success(Status);
    }
}
