using Pragmatic.Result;

namespace Pragmatic.MultiTenancy.Persistence.Errors;

/// <summary>
///     Error returned when a tenant is not found in the <see cref="ITenantStore" />.
/// </summary>
public sealed record TenantNotFoundError(string TenantId) : IError
{
    /// <inheritdoc />
    public string Code => "TENANT_NOT_FOUND";

    /// <inheritdoc />
    public int StatusCode => 404;

    /// <inheritdoc />
    public string Title => $"Tenant '{TenantId}' was not found in the tenant store.";
}
