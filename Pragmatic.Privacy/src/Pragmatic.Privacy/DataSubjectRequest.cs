namespace Pragmatic.Privacy;

/// <summary>
///     A request from a data subject, with the clock it has to be answered within.
/// </summary>
/// <remarks>
///     <para>
///         The deadline is part of the record rather than something a job works out later: a request
///         whose due date is computed on demand has no due date until someone asks, which is how a
///         statutory window is missed.
///     </para>
///     <para>
///         Transitions are guarded here rather than left to the caller. The state machine attribute in
///         <c>Pragmatic.Persistence</c> would validate them at compile time, and was not used on
///         purpose: it would make this module depend on a Layer-2 module, and the request lifecycle is
///         small enough that a declared transition table costs less than the coupling.
///     </para>
/// </remarks>
public sealed class DataSubjectRequest
{
    /// <summary>The transitions that are allowed, and by omission the ones that are not.</summary>
    private static readonly Dictionary<DataSubjectRequestStatus, DataSubjectRequestStatus[]> Allowed = new()
    {
        [DataSubjectRequestStatus.Received] =
            [DataSubjectRequestStatus.Verified, DataSubjectRequestStatus.Rejected],

        // Rejection stays available after verification: identity can be established and the request
        // still not apply — an erasure claim against data held under a legal obligation, for instance.
        [DataSubjectRequestStatus.Verified] =
            [DataSubjectRequestStatus.Executing, DataSubjectRequestStatus.Rejected],

        [DataSubjectRequestStatus.Executing] =
            [DataSubjectRequestStatus.Completed, DataSubjectRequestStatus.Partial],

        // Terminal. A completed erasure cannot be reopened: the data is gone, and pretending the
        // request is live again would promise something nobody can deliver.
        [DataSubjectRequestStatus.Completed] = [],
        [DataSubjectRequestStatus.Partial] = [],
        [DataSubjectRequestStatus.Rejected] = []
    };

    /// <summary>Identifier of the request.</summary>
    public required string RequestId { get; set; }

    /// <summary>The subject's opaque reference. Never their identity.</summary>
    public required string SubjectRef { get; set; }

    /// <summary>What is being asked for.</summary>
    public DataSubjectRequestType Type { get; set; }

    /// <summary>Where the request has got to.</summary>
    public DataSubjectRequestStatus Status { get; set; } = DataSubjectRequestStatus.Received;

    /// <summary>When the request was received — the moment the clock starts.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>When the answer is due.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>
    ///     Why the deadline was extended, when it was. An extension is itself a decision that has to be
    ///     justified and told to the subject, so an unexplained one is not allowed.
    /// </summary>
    public string? ExtensionReason { get; set; }

    /// <summary>When the request reached a terminal state.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    ///     What was retained and why, when the outcome was <see cref="DataSubjectRequestStatus.Partial" />.
    /// </summary>
    public string? RetentionSummary { get; set; }

    /// <summary>True once the request can no longer change.</summary>
    public bool IsClosed => Allowed[Status].Length == 0;

    /// <summary>Whether the deadline has passed without the request being closed.</summary>
    public bool IsOverdue(DateTimeOffset now) => !IsClosed && now > DueAt;

    /// <summary>Whether moving to <paramref name="next" /> is allowed from the current state.</summary>
    public bool CanTransitionTo(DataSubjectRequestStatus next) => Allowed[Status].Contains(next);

    /// <summary>
    ///     Moves the request to <paramref name="next" />, stamping the closing time when terminal.
    /// </summary>
    /// <exception cref="InvalidOperationException">The transition is not allowed.</exception>
    public void TransitionTo(DataSubjectRequestStatus next, DateTimeOffset now)
    {
        if (!CanTransitionTo(next))
            throw new InvalidOperationException(
                $"Request '{RequestId}' cannot move from {Status} to {next}. " +
                "The lifecycle is deliberately narrow: a request that can jump states is a request whose " +
                "record no longer describes what actually happened.");

        Status = next;

        if (IsClosed)
            ClosedAt = now;
    }

    /// <summary>
    ///     Extends the deadline, which is permitted once and only with a stated reason.
    /// </summary>
    /// <exception cref="ArgumentException">No reason was given.</exception>
    /// <exception cref="InvalidOperationException">The deadline was already extended, or the request is closed.</exception>
    public void ExtendDeadline(TimeSpan by, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (IsClosed)
            throw new InvalidOperationException($"Request '{RequestId}' is closed and its deadline cannot move.");

        if (ExtensionReason is not null)
            throw new InvalidOperationException(
                $"Request '{RequestId}' has already been extended once. A deadline that can be pushed " +
                "repeatedly is not a deadline.");

        ExtensionReason = reason;
        DueAt = DueAt.Add(by);
        _ = now;
    }

    /// <summary>
    ///     Opens a request, with the statutory window running from now.
    /// </summary>
    public static DataSubjectRequest Open(
        string requestId, string subjectRef, DataSubjectRequestType type, DateTimeOffset now, TimeSpan window)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        return new DataSubjectRequest
        {
            RequestId = requestId,
            SubjectRef = subjectRef,
            Type = type,
            Status = DataSubjectRequestStatus.Received,
            ReceivedAt = now,
            DueAt = now.Add(window)
        };
    }
}
