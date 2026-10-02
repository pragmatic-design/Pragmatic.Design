namespace Showcase.Host.FeatureFlags;

/// <summary>
/// Builds feature flag evaluation context from ambient HTTP request state.
/// Demonstrates: IFeatureFlagContextProvider combining ITenantContext + ICurrentUser.
/// </summary>
public class ShowcaseFeatureFlagContextProvider(
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IHostEnvironment hostEnvironment) : IFeatureFlagContextProvider
{
    public Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default)
    {
        // Plan is resolved from user claims (e.g., "plan" claim set by auth provider)
        var plan = currentUser.GetClaim("plan");

        var context = new FeatureFlagContext
        {
            TenantId = tenantContext.TenantId,
            UserId = currentUser.IsAuthenticated ? currentUser.Id : null,
            Plan = plan,
            Environment = hostEnvironment.EnvironmentName,
            Properties = new Dictionary<string, string>
            {
                ["isAuthenticated"] = currentUser.IsAuthenticated.ToString()
            }
        };

        return Task.FromResult(context);
    }
}
