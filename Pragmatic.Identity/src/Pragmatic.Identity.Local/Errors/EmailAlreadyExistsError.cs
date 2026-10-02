using Pragmatic.Result;

namespace Pragmatic.Identity.Local.Errors;

/// <summary>Email address is already registered.</summary>
/// <remarks>
///     The offending <see cref="Email"/> is echoed back by design so the caller can render an actionable
///     message. Note this confirms account existence to the requester: if your threat model treats
///     registration as an enumeration vector, return a generic acknowledgement from the endpoint instead
///     of serialising this error verbatim.
/// </remarks>
public sealed record EmailAlreadyExistsError(string Email) : IError
{
    public string Code => "EMAIL_ALREADY_EXISTS";
    public int StatusCode => 409;
    public string Title => "Email already exists";
}
