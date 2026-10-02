using Microsoft.Extensions.Logging;

namespace Pragmatic.MultiTenancy;

/// <summary>
///     Chain of responsibility resolver that tries multiple <see cref="ITenantResolver" /> instances
///     in order and returns the first non-null result.
/// </summary>
/// <remarks>
///     Resolvers are tried in registration order. This enables scenarios like:
///     try header first, fall back to claim, then to default.
///     Individual resolver failures are logged but do not stop the chain.
/// </remarks>
public sealed partial class CompositeTenantResolver(
    IEnumerable<ITenantResolver> resolvers,
    ILogger<CompositeTenantResolver> logger) : ITenantResolver
{
    // Cache resolver names at construction to avoid GetType().Name reflection on every resolution.
    private readonly (ITenantResolver Resolver, string Name)[] _resolvers =
        resolvers.Select(r => (r, r.GetType().Name)).ToArray();

    /// <inheritdoc />
    public async ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (resolver, resolverName) in _resolvers)
        {
            try
            {
                var tenantId = await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(tenantId))
                    return tenantId;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log failure but continue to next resolver in the chain
                LogResolverFailed(resolverName, ex);
            }
        }

        LogNoResolverSucceeded(_resolvers.Length);
        return null;
    }

    // =========================================================================
    // LoggerMessage - Zero Allocation Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Tenant resolver {ResolverName} failed, continuing to next resolver")]
    private partial void LogResolverFailed(string resolverName, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No tenant resolver succeeded out of {ResolverCount} registered resolver(s). Returning null tenant ID")]
    private partial void LogNoResolverSucceeded(int resolverCount);
}
