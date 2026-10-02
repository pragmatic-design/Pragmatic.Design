using Pragmatic;
using TimeOff.Leave.Events;

namespace TimeOff.Leave.Enums;

/// <summary>
///     Where a leave request is in its life: asked, then decided or withdrawn.
/// </summary>
/// <remarks>
///     Every move is declared here, and only these moves exist: a decision is taken on a pending
///     request, and a request is withdrawn while it is pending or approved — before it starts, which is
///     the operation's rule, not the machine's. A decision raises <see cref="LeaveRequestDecided" />.
/// </remarks>
[FastEnum]
public enum LeaveRequestStatus
{
    [InitialState]
    Pending,

    [TransitionFrom(LeaveRequestStatus.Pending)]
    [RaisesEvent<LeaveRequestDecided>]
    Approved,

    [TransitionFrom(LeaveRequestStatus.Pending)]
    [RaisesEvent<LeaveRequestDecided>]
    Rejected,

    [TransitionFrom(LeaveRequestStatus.Pending)]
    [TransitionFrom(LeaveRequestStatus.Approved)]
    Withdrawn
}
