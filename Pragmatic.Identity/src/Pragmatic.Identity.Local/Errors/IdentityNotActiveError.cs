using Pragmatic.Result;

namespace Pragmatic.Identity.Local.Errors;

/// <summary>Identity is not active (disabled by admin or deprovisioned).</summary>
public sealed record IdentityNotActiveError() : IError
{
    public string Code => "IDENTITY_NOT_ACTIVE";
    public int StatusCode => 403;
    public string Title => "Identity not active";
}
