namespace TimeOff.Leave.LeaveRequests.Mutations;

/// <summary>
///     A manager approves a pending request of their team.
/// </summary>
/// <remarks>
///     Another manager's request is not visible, so it is not found: the request is loaded through the
///     same row filter as every read. A request that is no longer pending is refused by the state
///     machine with a conflict.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(LeavePermissions.LeaveRequest.Decide)]
[Endpoint(HttpVerb.Post, "api/leave-requests/{id}/approve")]
[TransitionsTo<LeaveRequestStatus>(LeaveRequestStatus.Approved, When = TransitionTiming.ByBody)]
[ReturnsDto<LeaveRequestDto>]
[LoadCurrentUser]
public partial class ApproveLeaveRequestMutation : Mutation<LeaveRequest, ConflictError, CannotDecideOwnRequestError>
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

        var decided = entity.Decide(LeaveRequestStatus.Approved, _currentEmployee.Id, Note, Now);
        return Task.FromResult(decided.IsFailure ? Result<LeaveRequest, IError>.Failure(decided.Error) : Result<LeaveRequest, IError>.Success(entity));
    }
}
