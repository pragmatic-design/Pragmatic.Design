using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Resolution;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Configuration.Tests.Unit;

public class ConfigurationResolverTests
{
    private readonly InMemoryConfigurationStore _store = new();

    [Fact]
    public async Task ResolveAsync_BaseOnly_ReturnsBaseValue()
    {
        await _store.SetAsync("Booking:MaxGuests", "100");
        var resolver = CreateResolver("Production");

        var result = await resolver.ResolveAsync("Booking:MaxGuests");

        result.Should().Be("100");
    }

    [Fact]
    public async Task ResolveAsync_EnvironmentOverride_WinsOverBase()
    {
        await _store.SetAsync("Booking:MaxGuests", "100");
        await _store.SetAsync("staging/Booking:MaxGuests", "50");
        var resolver = CreateResolver("Staging");

        var result = await resolver.ResolveAsync("Booking:MaxGuests");

        result.Should().Be("50");
    }

    [Fact]
    public async Task ResolveAsync_TenantOverride_WinsOverAll()
    {
        await _store.SetAsync("Booking:MaxGuests", "100");
        await _store.SetAsync("staging/Booking:MaxGuests", "50");
        await _store.SetAsync("Booking:MaxGuests", "200", "acme");
        var resolver = CreateResolver("Staging", tenantId: "acme");

        var result = await resolver.ResolveAsync("Booking:MaxGuests");

        result.Should().Be("200");
    }

    [Fact]
    public async Task ResolveAsync_TenantResolvedButNoOverride_EnvironmentOverlayStillWins()
    {
        // Regression: a resolved tenant with no override for this key must NOT short-circuit the
        // cascade. The environment overlay has to keep winning over base.
        await _store.SetAsync("Booking:MaxGuests", "10");
        await _store.SetAsync("staging/Booking:MaxGuests", "20");
        // tenant "acme" has NO override for this key
        var resolver = CreateResolver("Staging", tenantId: "acme");

        var result = await resolver.ResolveAsync("Booking:MaxGuests");

        result.Should().Be("20");
    }

    [Fact]
    public async Task ResolveSectionAsync_TenantResolvedButNoOverride_KeepsEnvironmentOverlay()
    {
        await _store.SetAsync("Booking:MaxGuests", "10");
        await _store.SetAsync("staging/Booking:MaxGuests", "20");
        var resolver = CreateResolver("Staging", tenantId: "acme");

        var section = await resolver.ResolveSectionAsync("Booking:");

        section.Should().ContainKey("Booking:MaxGuests").WhoseValue.Should().Be("20");
    }

    [Fact]
    public async Task ResolveAsync_TenantNotResolved_SkipsTenantOverride()
    {
        await _store.SetAsync("Booking:MaxGuests", "100");
        await _store.SetAsync("Booking:MaxGuests", "200", "acme");
        var resolver = CreateResolver("Production"); // no tenant

        var result = await resolver.ResolveAsync("Booking:MaxGuests");

        result.Should().Be("100");
    }

    [Fact]
    public async Task ResolveAsync_TaggedEnvironment_MostSpecificWins()
    {
        await _store.SetAsync("Booking:MaxGuests", "100");
        await _store.SetAsync("staging/Booking:MaxGuests", "50");
        await _store.SetAsync("staging-eu-west/Booking:MaxGuests", "75");
        var resolver = CreateResolver("Staging", tag: "eu-west");

        var result = await resolver.ResolveAsync("Booking:MaxGuests");

        result.Should().Be("75");
    }

    [Fact]
    public async Task ResolveAsync_TaggedEnvironment_FallsBackToEnv()
    {
        await _store.SetAsync("Booking:MaxGuests", "100");
        await _store.SetAsync("staging/Booking:MaxGuests", "50");
        // No staging-eu-west override
        var resolver = CreateResolver("Staging", tag: "eu-west");

        var result = await resolver.ResolveAsync("Booking:MaxGuests");

        result.Should().Be("50");
    }

    [Fact]
    public async Task ResolveAsync_Production_NoEnvOverlay()
    {
        await _store.SetAsync("Booking:MaxGuests", "100");
        var resolver = CreateResolver("Production");

        // Production resolution chain is just ["base"] — no env overlay
        var result = await resolver.ResolveAsync("Booking:MaxGuests");

        result.Should().Be("100");
    }

    [Fact]
    public async Task ResolveAsync_MissingKey_ReturnsNull()
    {
        var resolver = CreateResolver("Production");

        var result = await resolver.ResolveAsync("NonExistent");

        result.Should().BeNull();
    }

    // Section resolution

    [Fact]
    public async Task ResolveSectionAsync_MergesBaseAndTenant()
    {
        await _store.SetAsync("Booking:MaxGuests", "100");
        await _store.SetAsync("Booking:HotelName", "Base Hotel");
        await _store.SetAsync("Booking:MaxGuests", "200", "acme");
        var resolver = CreateResolver("Production", tenantId: "acme");

        var section = await resolver.ResolveSectionAsync("Booking:");

        section.Should().ContainKey("Booking:MaxGuests").WhoseValue.Should().Be("200"); // tenant override
        section.Should().ContainKey("Booking:HotelName").WhoseValue.Should().Be("Base Hotel"); // base
    }

    // Helpers

    private ConfigurationResolver CreateResolver(
        string environment,
        string? tag = null,
        string? tenantId = null)
    {
        var profile = EnvironmentProfile.From(environment, tag);
        var tenantContext = tenantId is not null ? new FakeTenantContext(tenantId) : null;
        return new ConfigurationResolver(_store, profile, tenantContext);
    }

    private sealed class FakeTenantContext(string tenantId) : ITenantContext
    {
        public string? TenantId => tenantId;
        public string? TenantName => tenantId;
        public bool IsResolved => true;
    }
}
