namespace Pragmatic.MultiTenancy;

/// <summary>
///     Mutable, scoped implementation of <see cref="ITenantContext" />.
///     Updated by tenant resolution middleware or <see cref="TenantScope" />.
/// </summary>
/// <remarks>
///     Register as scoped. The middleware or resolver sets <see cref="TenantId" />
///     early in the pipeline; downstream services read it via <see cref="ITenantContext" />.
/// </remarks>
public sealed class MutableTenantContext : IMutableTenantContext
{
    /// <summary>
    ///     The resolved tenant identifier. Null when no tenant is resolved.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    ///     Human-readable tenant name. Null when no tenant is resolved.
    /// </summary>
    public string? TenantName { get; set; }

    /// <inheritdoc />
    public bool IsResolved => !string.IsNullOrEmpty(TenantId);

    /// <inheritdoc />
    public IDisposable SetTenant(string? tenantId, string? tenantName = null)
    {
        var previousId = TenantId;
        var previousName = TenantName;
        TenantId = tenantId;
        TenantName = tenantName;
        return new TenantRestore(this, previousId, previousName);
    }

    private sealed class TenantRestore(MutableTenantContext context, string? previousId, string? previousName)
        : IDisposable
    {
        public void Dispose()
        {
            context.TenantId = previousId;
            context.TenantName = previousName;
        }
    }
}
