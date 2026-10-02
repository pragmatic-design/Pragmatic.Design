namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Thrown when a request is made for a deactivated tenant.
/// </summary>
public sealed class TenantDeactivatedException(string tenantId)
    : InvalidOperationException($"Tenant '{tenantId}' has been deactivated.");
