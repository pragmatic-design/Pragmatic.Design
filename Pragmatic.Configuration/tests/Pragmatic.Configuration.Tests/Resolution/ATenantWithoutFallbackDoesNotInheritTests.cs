using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Extensions;
using Pragmatic.Configuration.Resolution;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Configuration.Tests.Resolution;

/// <summary>
///     With <c>MultiTenant.FallbackToBase = false</c>, a tenant with no override of its own does not
///     inherit the base value.
/// </summary>
/// <remarks>
///     The option was declared, documented, set by the Showcase — and read by nothing: every tenant fell
///     back to the base value whatever it said. Measured through the registration an application uses,
///     so the option has to reach the resolver, not only the resolver answer correctly.
/// </remarks>
public class ATenantWithoutFallbackDoesNotInheritTests
{
    [Fact]
    public async Task WithoutFallback_ATenantWithNoOverride_GetsNothing()
    {
        var resolver = await BuildAsync(fallbackToBase: false);

        (await resolver.ResolveAsync("Mail:From")).Should().BeNull();
        (await resolver.ResolveSectionAsync("Mail:")).Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutFallback_ATenantsOwnOverride_StillApplies()
    {
        var resolver = await BuildAsync(fallbackToBase: false, tenantOverride: "acme@example.com");

        (await resolver.ResolveAsync("Mail:From")).Should().Be("acme@example.com");
    }

    /// <summary>The control: by default a tenant inherits the base value.</summary>
    [Fact]
    public async Task WithFallback_ATenantWithNoOverride_InheritsTheBase()
    {
        var resolver = await BuildAsync(fallbackToBase: true);

        (await resolver.ResolveAsync("Mail:From")).Should().Be("base@example.com");
    }

    private static async Task<IConfigurationResolver> BuildAsync(bool fallbackToBase, string? tenantOverride = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => new Tenant("acme"));
        services.AddPragmaticConfiguration(o =>
        {
            o.MultiTenant.Enabled = true;
            o.MultiTenant.FallbackToBase = fallbackToBase;
        });

        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IConfigurationStore>();
        await store.SetAsync("Mail:From", "base@example.com");
        if (tenantOverride is not null)
            await store.SetAsync("Mail:From", tenantOverride, "acme");

        return provider.CreateScope().ServiceProvider.GetRequiredService<IConfigurationResolver>();
    }

    private sealed class Tenant(string id) : ITenantContext
    {
        public string? TenantId => id;
        public string? TenantName => id;
        public bool IsResolved => true;
    }
}
