namespace Pragmatic.MultiTenancy;

/// <summary>
///     The effective <see cref="ITenantContext"/>: prefers the request-scoped tenant (set by the
///     resolution middleware on <see cref="MutableTenantContext"/>) and falls back to the ambient
///     <see cref="TenantScope"/> (AsyncLocal) when no request tenant is resolved — so a background job,
///     seed, or test that opens <c>TenantScope.BeginScope(...)</c> is actually honored.
/// </summary>
/// <remarks>
///     <see cref="MutableTenantContext"/> alone never reads <see cref="TenantScope"/>'s AsyncLocal, so
///     resolving it as the <see cref="ITenantContext"/> would leave a background-job scope inert. This
///     composite is what makes the scope take effect.
/// </remarks>
public sealed class AmbientTenantContext(MutableTenantContext request) : ITenantContext
{
    // Reads the static AsyncLocal owned by TenantScope; one shared instance is enough.
    private static readonly TenantScope Ambient = new();

    /// <inheritdoc />
    public string? TenantId => request.IsResolved ? request.TenantId : Ambient.TenantId;

    /// <inheritdoc />
    public string? TenantName => request.IsResolved ? request.TenantName : Ambient.TenantName;

    /// <inheritdoc />
    public bool IsResolved => request.IsResolved || Ambient.IsResolved;
}
