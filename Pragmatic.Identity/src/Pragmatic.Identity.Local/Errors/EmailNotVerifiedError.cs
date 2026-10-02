using Pragmatic.Result;

namespace Pragmatic.Identity.Local.Errors;

/// <summary>Login refused because the account's email has not been verified.</summary>
public sealed record EmailNotVerifiedError() : IError
{
    public string Code => "EMAIL_NOT_VERIFIED";
    public int StatusCode => 403;
    public string Title => "Email not verified";
}
