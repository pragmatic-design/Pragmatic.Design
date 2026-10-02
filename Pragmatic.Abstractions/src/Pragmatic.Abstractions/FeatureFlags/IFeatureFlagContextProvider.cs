namespace Pragmatic.FeatureFlags;

/// <summary>
///     Resolves the feature flag evaluation context from the current request/scope.
///     Implementations typically combine ITenantContext, ICurrentUser, and environment info.
/// </summary>
public interface IFeatureFlagContextProvider
{
    /// <summary>
    ///     Builds the current evaluation context from ambient state (HTTP request, tenant, user, etc.)
    /// </summary>
    Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default);
}
