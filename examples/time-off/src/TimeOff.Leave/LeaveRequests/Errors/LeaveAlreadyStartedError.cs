namespace TimeOff.Leave.Errors;

/// <summary>
///     A request is withdrawn before its first day; from then on it is leave being taken.
/// </summary>
/// <remarks>The words are in <c>translations/*.json</c>, under <c>error.leave.already.started</c>.</remarks>
public sealed partial record LeaveAlreadyStartedError : Error
{
    public override string Code => "LEAVE_ALREADY_STARTED";
    public override int StatusCode => 409;

    public DateOnly From { get; init; }
}
