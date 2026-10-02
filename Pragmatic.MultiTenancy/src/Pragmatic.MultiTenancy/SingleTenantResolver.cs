namespace Pragmatic.MultiTenancy;

/// <summary>
///     Default resolver for single-tenant applications.
///     Always returns a fixed tenant ID with zero overhead.
/// </summary>
/// <remarks>
///     This is the default when no multi-tenancy strategy is configured.
///     A single-tenant app is simply a multi-tenant app with one tenant.
/// </remarks>
public sealed class SingleTenantResolver(string tenantId = "default") : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>(tenantId);
}
