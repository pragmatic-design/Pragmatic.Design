namespace TimeOff.Leave.Errors;

/// <summary>
///     Only the employee who asked withdraws a request — their manager sees it, and decides it, but
///     does not take it back for them.
/// </summary>
/// <remarks>The words are in <c>translations/*.json</c>, under <c>error.not.your.request</c>.</remarks>
public sealed partial record NotYourRequestError : Error
{
    public override string Code => "NOT_YOUR_REQUEST";
    public override int StatusCode => 403;
}
