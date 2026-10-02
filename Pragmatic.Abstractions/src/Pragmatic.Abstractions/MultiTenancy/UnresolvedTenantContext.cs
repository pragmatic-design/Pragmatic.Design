namespace Pragmatic.MultiTenancy;

/// <summary>
///     Default <see cref="ITenantContext" /> when no tenant resolution has occurred.
///     All properties return <c>null</c> and <see cref="IsResolved" /> is <c>false</c>.
/// </summary>
/// <remarks>
///     Register as the fallback when no tenant resolution is configured:
///     <code>services.AddSingleton&lt;ITenantContext&gt;(UnresolvedTenantContext.Instance);</code>
/// </remarks>
public sealed class UnresolvedTenantContext : ITenantContext
{
    /// <summary>
    ///     Singleton instance. Use this instead of creating new instances.
    /// </summary>
    public static readonly UnresolvedTenantContext Instance = new();

    private UnresolvedTenantContext() { }

    /// <inheritdoc />
    public string? TenantId => null;

    /// <inheritdoc />
    public string? TenantName => null;

    /// <inheritdoc />
    public bool IsResolved => false;
}
