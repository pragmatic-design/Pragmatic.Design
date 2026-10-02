namespace TimeOff.Leave.Infrastructure.Audit;

/// <summary>
///     The names the leave decisions are recorded under in the audit trail — constants, never assembled
///     at runtime, so a reader can search for them.
/// </summary>
public static class LeaveAuditOperations
{
    public const string RequestApproved = "Leave.RequestApproved";
    public const string RequestRejected = "Leave.RequestRejected";

    /// <summary>What the audit trail calls a leave request.</summary>
    public const string LeaveRequestTarget = "LeaveRequest";

    public static string For(LeaveRequestStatus decision) => decision switch
    {
        LeaveRequestStatus.Approved => RequestApproved,
        LeaveRequestStatus.Rejected => RequestRejected,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Only a decision is recorded.")
    };
}
