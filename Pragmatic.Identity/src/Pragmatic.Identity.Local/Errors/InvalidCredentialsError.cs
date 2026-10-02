using Pragmatic.Result;

namespace Pragmatic.Identity.Local.Errors;

/// <summary>Invalid email or password.</summary>
public sealed record InvalidCredentialsError() : IError
{
    public string Code => "INVALID_CREDENTIALS";
    public int StatusCode => 401;
    public string Title => "Invalid credentials";
}
