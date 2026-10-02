using Pragmatic.Audit;
using TimeOff.Leave.Infrastructure.Audit;

namespace TimeOff.Leave.LeaveRequests.Actions;

/// <summary>
///     The decisions taken on a leave request, as the audit trail recorded them — for whoever may see
///     every request.
/// </summary>
/// <remarks>
///     An action and not a query: the answer comes from the audit trail, not from the request's table.
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.LeaveRequest.ViewAll)]
[LoadEntity<LeaveRequest>(nameof(Id))]
[Endpoint(HttpVerb.Get, "api/leave-requests/{id}/decisions")]
public partial class GetLeaveDecisionsAction : DomainAction<IReadOnlyList<LeaveDecisionDto>, NotFoundError>
{
    private IAuditTrailReader _trail = null!;

    [FromRoute]
    public required Guid Id { get; init; }

    public override async Task<Result<IReadOnlyList<LeaveDecisionDto>, IError>> Execute(CancellationToken ct = default)
    {
        var page = await _trail.QueryAsync(new AuditQuery
        {
            TargetType = LeaveAuditOperations.LeaveRequestTarget,
            TargetId = Id.ToString("N")
        }, ct).ConfigureAwait(false);

        return page.Entries
            .OrderBy(e => e.OccurredAt)
            .Select(LeaveDecisionDto.Selector)
            .ToList();
    }
}
