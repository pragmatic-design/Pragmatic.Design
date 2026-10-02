namespace TimeOff.Leave.LeaveRequests.Mutations;

/// <summary>
///     An employee takes back a request of theirs, pending or approved, until its first day. What it
///     took from the allowance is given back.
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(LeavePermissions.LeaveRequest.Withdraw)]
[Endpoint(HttpVerb.Post, "api/leave-requests/{id}/withdraw")]
// AfterBody: the two refusals below keep their own codes; the move — and its 409 when the request is
// already decided the other way — comes after them.
[TransitionsTo<LeaveRequestStatus>(LeaveRequestStatus.Withdrawn, When = TransitionTiming.AfterBody)]
[ReturnsDto<LeaveRequestDto>]
[LoadCurrentUser]
public partial class WithdrawLeaveRequestMutation
    : Mutation<LeaveRequest, ConflictError, NotYourRequestError, LeaveAlreadyStartedError>
{
    public required Guid Id { get; init; }

    /// <summary>The day it is withdrawn: written by the invoker from the application's clock.</summary>
    [FromClock]
    public DateOnly Today { get; private set; }

    public override Task<Result<LeaveRequest, IError>> ApplyAsync(LeaveRequest entity, CancellationToken ct = default)
    {
        if (_currentEmployee.Id != entity.EmployeeId)
            return Task.FromResult<Result<LeaveRequest, IError>>(new NotYourRequestError());

        if (entity.From <= Today)
            return Task.FromResult<Result<LeaveRequest, IError>>(new LeaveAlreadyStartedError { From = entity.From });

        return Task.FromResult(Result<LeaveRequest, IError>.Success(entity));
    }
}
