namespace TimeOff.Leave.Errors;

/// <summary>
///     A manager decides their team's requests, never their own.
/// </summary>
/// <remarks>The words are in <c>translations/*.json</c>, under <c>error.cannot.decide.own.request</c>.</remarks>
public sealed partial record CannotDecideOwnRequestError : Error
{
    public override string Code => "CANNOT_DECIDE_OWN_REQUEST";
    public override int StatusCode => 403;
}
