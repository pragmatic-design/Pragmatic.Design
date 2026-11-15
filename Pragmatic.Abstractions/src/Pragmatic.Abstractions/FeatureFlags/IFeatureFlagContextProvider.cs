namespace Pragmatic.FeatureFlags;

/// <summary>
///     Resolves the feature flag evaluation context from the current request/scope.
///     Implementations typically combine ITenantContext, ICurrentUser, and environment info.
/// </summary>
/// <remarks>
///     <c>AddPragmaticFeatureFlags</c> registers <c>TenantFeatureFlagContextProvider</c> when the application
///     registers none, so a module may inject it.
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IFeatureFlagContextProvider
{
    /// <summary>
    ///     Builds the current evaluation context from ambient state (HTTP request, tenant, user, etc.)
    /// </summary>
    Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default);
}
