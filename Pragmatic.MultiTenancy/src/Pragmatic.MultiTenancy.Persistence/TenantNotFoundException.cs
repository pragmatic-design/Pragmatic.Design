namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Thrown when a tenant is not found in the <see cref="ITenantStore"/>.
/// </summary>
public sealed class TenantNotFoundException(string tenantId)
    : InvalidOperationException($"Tenant '{tenantId}' not found in the tenant store.");
