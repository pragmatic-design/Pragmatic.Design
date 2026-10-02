namespace Pragmatic.MultiTenancy;

/// <summary>
///     AsyncLocal-based tenant scope for non-HTTP contexts (background jobs, tests, seed).
///     Sets the tenant context for the current async flow.
/// </summary>
/// <remarks>
///     <para>
///     Pattern identical to <c>IQueryFilterToggle</c> — uses <see cref="AsyncLocal{T}" /> for
///     ambient context that flows through async continuations.
///     </para>
///     <para>
///     Usage:
///     <code>
///     using var scope = TenantScope.BeginScope("tenant-123");
///     // ITenantContext.TenantId is now "tenant-123" within this async flow
///     </code>
///     </para>
/// </remarks>
public sealed class TenantScope : ITenantContext
{
    private static readonly AsyncLocal<TenantScopeState?> CurrentState = new();

    /// <inheritdoc />
    public string? TenantId => CurrentState.Value?.TenantId;

    /// <inheritdoc />
    public string? TenantName => CurrentState.Value?.TenantName;

    /// <inheritdoc />
    public bool IsResolved => CurrentState.Value is not null;

    /// <summary>
    ///     Begins a tenant scope for the current async flow.
    ///     Dispose the returned handle to restore the previous scope.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to set.</param>
    /// <param name="tenantName">Optional human-readable tenant name.</param>
    /// <returns>A disposable that restores the previous scope on dispose.</returns>
    public static IDisposable BeginScope(string tenantId, string? tenantName = null)
    {
        var previous = CurrentState.Value;
        CurrentState.Value = new TenantScopeState(tenantId, tenantName);
        return new ScopeDisposable(previous);
    }

    private sealed record TenantScopeState(string TenantId, string? TenantName);

    private sealed class ScopeDisposable(TenantScopeState? previous) : IDisposable
    {
        public void Dispose() => CurrentState.Value = previous;
    }
}
