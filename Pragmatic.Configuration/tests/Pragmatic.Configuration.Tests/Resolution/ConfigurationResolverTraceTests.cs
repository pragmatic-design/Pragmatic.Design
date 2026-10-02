using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization;
using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Resolution;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Xunit;

namespace Pragmatic.Configuration.Tests.Resolution;

/// <summary>
///     Verifies <see cref="ConfigurationResolver.ResolveWithTraceAsync" />: the resolved value carries the
///     provenance of <b>which</b> cascade layer supplied it (user → tenant → environment → base), so the
///     management surface can answer "why is this value what it is?".
/// </summary>
public class ConfigurationResolverTraceTests
{
    private sealed class FakeTenantContext(string? tenantId) : ITenantContext
    {
        public string? TenantId => tenantId;
        public string? TenantName => tenantId;
        public bool IsResolved => tenantId is not null;
    }

    private sealed class FakeCurrentUser(string id) : ICurrentUser
    {
        public string Id => id;
        public string? DisplayName => id;
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; } =
            new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    [Fact]
    public async Task ResolveWithTrace_KeyUnsetEverywhere_ReportsNotFound()
    {
        var store = new InMemoryConfigurationStore();
        var resolver = new ConfigurationResolver(store, EnvironmentProfile.From("Production"));

        var trace = await resolver.ResolveWithTraceAsync("Missing:Key");

        trace.Found.Should().BeFalse();
        trace.Source.Should().Be(ResolutionSource.NotFound);
        trace.Value.Should().BeNull();
    }

    [Fact]
    public async Task ResolveWithTrace_OnlyBaseSet_ReportsBase()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Feature:Timeout", "30");
        var resolver = new ConfigurationResolver(store, EnvironmentProfile.From("Production"));

        var trace = await resolver.ResolveWithTraceAsync("Feature:Timeout");

        trace.Value.Should().Be("30");
        trace.Source.Should().Be(ResolutionSource.Base);
        trace.SourceKey.Should().Be("Feature:Timeout");
        trace.EnvironmentTag.Should().BeNull();
    }

    [Fact]
    public async Task ResolveWithTrace_EnvironmentOverlay_WinsOverBase_AndReportsTag()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Feature:Timeout", "30");             // base
        await store.SetAsync("staging/Feature:Timeout", "10");     // staging overlay
        var resolver = new ConfigurationResolver(store, EnvironmentProfile.From("Staging"));

        var trace = await resolver.ResolveWithTraceAsync("Feature:Timeout");

        trace.Value.Should().Be("10");
        trace.Source.Should().Be(ResolutionSource.Environment);
        trace.SourceKey.Should().Be("staging/Feature:Timeout");
        trace.EnvironmentTag.Should().Be("staging");
    }

    [Fact]
    public async Task ResolveWithTrace_TenantOverride_WinsOverEnvironmentAndBase()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Feature:Timeout", "30");             // base
        await store.SetAsync("staging/Feature:Timeout", "10");     // env overlay
        await store.SetAsync("Feature:Timeout", "5", "tenant-a");  // tenant override
        var resolver = new ConfigurationResolver(
            store, EnvironmentProfile.From("Staging"), new FakeTenantContext("tenant-a"));

        var trace = await resolver.ResolveWithTraceAsync("Feature:Timeout");

        trace.Value.Should().Be("5");
        trace.Source.Should().Be(ResolutionSource.Tenant);
        trace.SourceKey.Should().Be("tenant-a");
    }

    [Fact]
    public async Task ResolveWithTrace_UserOverride_HasHighestPrecedence()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Feature:Timeout", "30");                  // base
        await store.SetAsync("Feature:Timeout", "5", "tenant-a");       // tenant override
        await store.SetAsync("Feature:Timeout", "1", "user:u-42");      // user override
        var resolver = new ConfigurationResolver(
            store,
            EnvironmentProfile.From("Production"),
            new FakeTenantContext("tenant-a"),
            new FakeCurrentUser("u-42"));

        var trace = await resolver.ResolveWithTraceAsync("Feature:Timeout");

        trace.Value.Should().Be("1");
        trace.Source.Should().Be(ResolutionSource.User);
        trace.SourceKey.Should().Be("user:u-42");
    }

    [Fact]
    public async Task ResolveAsync_ReturnsSameValueAsTrace()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Feature:Timeout", "30");
        await store.SetAsync("staging/Feature:Timeout", "10");
        var resolver = new ConfigurationResolver(store, EnvironmentProfile.From("Staging"));

        var plain = await resolver.ResolveAsync("Feature:Timeout");
        var trace = await resolver.ResolveWithTraceAsync("Feature:Timeout");

        plain.Should().Be(trace.Value).And.Be("10");
    }
}
