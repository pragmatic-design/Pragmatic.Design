using Microsoft.Extensions.DependencyInjection;
using Pragmatic.FeatureFlags;
using Pragmatic.FeatureFlags.Providers;

namespace Pragmatic.FeatureFlags.Samples;

/// <summary>
/// Demonstrates <see cref="IFeatureFlagContextProvider"/>: an ambient source of the
/// current <see cref="FeatureFlagContext"/> (e.g. derived from the signed-in user /
/// HTTP request) so call sites evaluate flags without threading context manually.
/// </summary>
public static class ContextProviderSample
{
    /// <summary>
    /// A sample provider. In a real app this would read the current user/tenant/plan
    /// from <c>IHttpContextAccessor</c>, <c>ICurrentUser</c> or an async-local scope;
    /// here it is fixed so the sample is deterministic.
    /// </summary>
    private sealed class CurrentUserContextProvider : IFeatureFlagContextProvider
    {
        public Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default)
            => Task.FromResult(new FeatureFlagContext
            {
                UserId = "user-99",
                Plan = "enterprise",
                Environment = "Production",
            });
    }

    public static async Task RunAsync()
    {
        var services = new ServiceCollection();
        services.AddPragmaticFeatureFlags();
        services.AddSingleton<IFeatureFlagContextProvider, CurrentUserContextProvider>();

        await using var provider = services.BuildServiceProvider();

        // AddPragmaticFeatureFlags registers the concrete store too, so seeders can call Define.
        var seedStore = provider.GetRequiredService<InMemoryFeatureFlagStore>();
        seedStore.Define(new FeatureFlagDefinition
        {
            Name = "premium-reports",
            Enabled = false,
            Rules = [new FeatureFlagRule { Type = "plan", Values = ["enterprise"], Enabled = true }],
        });

        var store = provider.GetRequiredService<IFeatureFlagStore>();
        var contextProvider = provider.GetRequiredService<IFeatureFlagContextProvider>();

        // Resolve the ambient context, then evaluate against it.
        var context = await contextProvider.GetContextAsync();
        var enabled = await store.IsEnabledAsync("premium-reports", context);

        Console.WriteLine($"  ambient context: UserId={context.UserId}, Plan={context.Plan}");
        Console.WriteLine($"  premium-reports (resolved ambiently) -> {enabled}");
    }
}
