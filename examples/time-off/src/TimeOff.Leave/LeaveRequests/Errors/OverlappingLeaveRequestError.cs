namespace TimeOff.Leave.Errors;

/// <summary>
///     The period overlaps a request of the same employee that is pending or approved.
/// </summary>
/// <remarks>
///     The words are in <c>translations/*.json</c>, under <c>error.leave.request.overlaps</c>: the code
///     names the refusal, the reader's language says it.
/// </remarks>
public sealed partial record OverlappingLeaveRequestError : Error
{
    public override string Code => "LEAVE_REQUEST_OVERLAPS";
    public override int StatusCode => 409;

    /// <summary>The request already covering part of the period.</summary>
    public Guid OverlappingRequestId { get; init; }
}
