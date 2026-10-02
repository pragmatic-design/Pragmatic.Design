using Pragmatic.Result;

namespace Pragmatic.Identity.Local.Errors;

/// <summary>Password does not meet the password policy requirements.</summary>
public sealed record PasswordPolicyError(string Reason) : IError
{
    public string Code => "PASSWORD_POLICY_VIOLATION";
    public int StatusCode => 422;
    public string Title => Reason;
}
