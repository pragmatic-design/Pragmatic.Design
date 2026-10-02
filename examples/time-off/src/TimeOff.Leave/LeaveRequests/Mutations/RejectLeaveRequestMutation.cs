namespace TimeOff.Leave.LeaveRequests.Mutations;

/// <summary>
///     A manager rejects a pending request of their team, saying why.
/// </summary>
/// <remarks>
///     Visibility and the state machine answer as for approving: another team's request is not found,
///     a request no longer pending is a conflict.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(LeavePermissions.LeaveRequest.Decide)]
[Endpoint(HttpVerb.Post, "api/leave-requests/{id}/reject")]
[TransitionsTo<LeaveRequestStatus>(LeaveRequestStatus.Rejected, When = TransitionTiming.ByBody)]
[ReturnsDto<LeaveRequestDto>]
[LoadCurrentUser]
public partial class RejectLeaveRequestMutation : Mutation<LeaveRequest, ConflictError, CannotDecideOwnRequestError>
{
    public required Guid Id { get; init; }

    /// <summary>When the decision is taken: written by the invoker from the application's clock.</summary>
    [FromClock]
    public DateTimeOffset Now { get; private set; }

    [MapIgnore]
    public string? Note { get; init; }

    public override Task<Result<LeaveRequest, IError>> ApplyAsync(LeaveRequest entity, CancellationToken ct = default)
    {
        if (_currentEmployee.Id == entity.EmployeeId)
            return Task.FromResult<Result<LeaveRequest, IError>>(new CannotDecideOwnRequestError());

        var decided = entity.Decide(LeaveRequestStatus.Rejected, _currentEmployee.Id, Note, Now);
        return Task.FromResult(decided.IsFailure ? Result<LeaveRequest, IError>.Failure(decided.Error) : Result<LeaveRequest, IError>.Success(entity));
    }
}
