using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.FeatureFlags.Providers;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests;

/// <summary>
///     <see cref="IFeatureFlags"/> closes the gap that left <see cref="IFeatureFlagContextProvider"/>
///     declared but wired to nothing: the provider is now consumed by the runtime instead of every call site
///     having to resolve it and thread the context through by hand.
/// </summary>
public class ContextualFeatureFlagsTests
{
    private sealed class FixedContextProvider(FeatureFlagContext context) : IFeatureFlagContextProvider
    {
        public Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default)
            => Task.FromResult(context);
    }

    private sealed class PremiumOnly : IFeatureFlag
    {
        public static string Name => "premium-only";
        public static string? Description => null;
    }

    private static ServiceProvider BuildProvider(IFeatureFlagContextProvider? contextProvider)
    {
        var services = new ServiceCollection();
        services.AddPragmaticFeatureFlags();
        if (contextProvider is not null)
            services.AddScoped(_ => contextProvider);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<InMemoryFeatureFlagStore>().Define(new FeatureFlagDefinition
        {
            Name = PremiumOnly.Name,
            Enabled = false,
            Rules = [FeatureFlagRule.Plan("premium")]
        });

        return provider;
    }

    [Fact]
    public async Task IsEnabledAsync_UsesTheAmbientContext_WithoutTheCallerBuildingIt()
    {
        using var provider = BuildProvider(new FixedContextProvider(new FeatureFlagContext { Plan = "premium" }));
        using var scope = provider.CreateScope();

        var flags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        (await flags.IsEnabledAsync<PremiumOnly>()).Should().BeTrue();
        (await flags.IsEnabledAsync(PremiumOnly.Name)).Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_HonoursAContextThatDoesNotMatch()
    {
        using var provider = BuildProvider(new FixedContextProvider(new FeatureFlagContext { Plan = "free" }));
        using var scope = provider.CreateScope();

        var flags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        (await flags.IsEnabledAsync<PremiumOnly>()).Should().BeFalse();
    }

    [Fact]
    public async Task WithoutAContextProvider_FallsBackToEmpty_InsteadOfFailing()
    {
        using var provider = BuildProvider(contextProvider: null);
        using var scope = provider.CreateScope();

        var flags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        (await flags.GetContextAsync()).Should().BeSameAs(FeatureFlagContext.Empty);
        (await flags.IsEnabledAsync<PremiumOnly>()).Should()
            .BeFalse("with no context the targeting rule cannot match, so the global state applies");
    }

    [Fact]
    public async Task GetContextAsync_ExposesTheContextUsedForEvaluation()
    {
        var context = new FeatureFlagContext { TenantId = "acme", UserId = "u1", Plan = "premium" };
        using var provider = BuildProvider(new FixedContextProvider(context));
        using var scope = provider.CreateScope();

        var flags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        (await flags.GetContextAsync()).Should().BeSameAs(context);
    }

    [Fact]
    public void AddPragmaticFeatureFlags_RegistersIFeatureFlags()
    {
        var services = new ServiceCollection();

        services.AddPragmaticFeatureFlags();

        services.Should().Contain(d => d.ServiceType == typeof(IFeatureFlags));
    }
}
