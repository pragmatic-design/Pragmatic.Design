using Pragmatic.Result;

namespace Pragmatic.Identity.Local.Errors;

/// <summary>Account is locked due to too many failed login attempts.</summary>
public sealed record AccountLockedError(DateTimeOffset? LockedUntil) : IError
{
    public string Code => "ACCOUNT_LOCKED";
    public int StatusCode => 423;
    public string Title => "Account locked";
}
