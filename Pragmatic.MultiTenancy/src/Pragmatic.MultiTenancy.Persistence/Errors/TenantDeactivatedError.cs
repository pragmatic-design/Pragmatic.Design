using Pragmatic.Result;

namespace Pragmatic.MultiTenancy.Persistence.Errors;

/// <summary>
///     Error returned when a request is made for a deactivated tenant.
/// </summary>
public sealed record TenantDeactivatedError(string TenantId) : IError
{
    /// <inheritdoc />
    public string Code => "TENANT_DEACTIVATED";

    /// <inheritdoc />
    public int StatusCode => 403;

    /// <inheritdoc />
    public string Title => $"Tenant '{TenantId}' has been deactivated.";
}
