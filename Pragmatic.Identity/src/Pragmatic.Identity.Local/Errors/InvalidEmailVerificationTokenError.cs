using Pragmatic.Result;

namespace Pragmatic.Identity.Local.Errors;

/// <summary>The supplied email-verification token is invalid, expired, or already used.</summary>
public sealed record InvalidEmailVerificationTokenError() : IError
{
    public string Code => "INVALID_EMAIL_VERIFICATION_TOKEN";
    public int StatusCode => 400;
    public string Title => "Invalid email verification token";
}
