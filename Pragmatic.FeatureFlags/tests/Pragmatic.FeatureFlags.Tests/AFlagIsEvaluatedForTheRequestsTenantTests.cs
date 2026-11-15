using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.FeatureFlags.Tests;

/// <summary>
///     <c>AddPragmaticFeatureFlags</c> evaluates against the request's tenant when the application registers no
///     context of its own (#747), so a tenant targeting rule turns a flag on for that tenant only.
/// </summary>
public class AFlagIsEvaluatedForTheRequestsTenantTests
{
    [Fact]
    public async Task ATenantTargetedFlag_IsOnForThatTenant_AndOffForAnother()
    {
        (await IsOnFor("acme")).Should().BeTrue("the rule targets acme");
        (await IsOnFor("globex")).Should().BeFalse("and no other tenant");
    }

    /// <summary>The control: without multi-tenancy the context is empty, as it was, and a tenant rule matches nothing.</summary>
    [Fact]
    public async Task WithoutATenant_TheContextIsEmpty()
    {
        var services = new ServiceCollection().AddPragmaticFeatureFlags();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = await scope.ServiceProvider.GetRequiredService<IFeatureFlags>().GetContextAsync();

        context.Should().Be(FeatureFlagContext.Empty);
    }

    /// <summary>A context the application registers is the one used.</summary>
    [Fact]
    public async Task AContextTheApplicationRegisters_IsTheOneUsed()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFeatureFlagContextProvider, PlanContext>();
        services.AddPragmaticFeatureFlags();
        services.AddSingleton<ITenantContext>(new Tenant("acme"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = await scope.ServiceProvider.GetRequiredService<IFeatureFlags>().GetContextAsync();

        context.Plan.Should().Be("gold");
    }

    private static async Task<bool> IsOnFor(string tenant)
    {
        var services = new ServiceCollection().AddPragmaticFeatureFlags();
        services.AddSingleton<ITenantContext>(new Tenant(tenant));
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Pragmatic.FeatureFlags.Providers.InMemoryFeatureFlagStore>()
            .Define(new FeatureFlagDefinition { Name = "escalations", Enabled = false, Rules = [FeatureFlagRule.Tenant("acme")] });

        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFeatureFlags>().IsEnabledAsync("escalations");
    }

    private sealed class Tenant(string id) : ITenantContext
    {
        public string? TenantId => id;
        public string? TenantName => id;
        public bool IsResolved => true;
    }

    private sealed class PlanContext : IFeatureFlagContextProvider
    {
        public Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default)
            => Task.FromResult(FeatureFlagContext.Empty with { Plan = "gold" });
    }
}
