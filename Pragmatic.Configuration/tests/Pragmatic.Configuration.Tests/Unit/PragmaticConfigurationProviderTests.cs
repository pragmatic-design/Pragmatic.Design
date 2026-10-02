using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Bridge;
using Pragmatic.Configuration.Providers;

namespace Pragmatic.Configuration.Tests.Unit;

public class PragmaticConfigurationProviderTests
{
    private readonly InMemoryConfigurationStore _store = new();

    [Fact]
    public void Load_EmptyStore_ReturnsEmptyData()
    {
        var env = EnvironmentProfile.From("Production");
        using var provider = new PragmaticConfigurationProvider(_store, env, keyPrefix: null);

        provider.Load();

        provider.TryGet("any:key", out var value);
        value.Should().BeNull();
    }

    [Fact]
    public async Task Load_WithValues_ExposesThemViaStandardApi()
    {
        await _store.SetAsync("App:Timeout", "30");
        await _store.SetAsync("App:Name", "MyApp");

        var env = EnvironmentProfile.From("Production");
        using var provider = new PragmaticConfigurationProvider(_store, env, keyPrefix: null);

        provider.Load();

        provider.TryGet("App:Timeout", out var timeout).Should().BeTrue();
        timeout.Should().Be("30");

        provider.TryGet("App:Name", out var name).Should().BeTrue();
        name.Should().Be("MyApp");
    }

    [Fact]
    public async Task Load_WithPrefix_OnlyLoadsMatchingKeys()
    {
        await _store.SetAsync("App:Timeout", "30");
        await _store.SetAsync("Other:Setting", "value");

        var env = EnvironmentProfile.From("Production");
        using var provider = new PragmaticConfigurationProvider(_store, env, keyPrefix: "App:");

        provider.Load();

        provider.TryGet("App:Timeout", out var timeout).Should().BeTrue();
        timeout.Should().Be("30");

        provider.TryGet("Other:Setting", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Load_EnvironmentOverlay_AppliesOverrides()
    {
        await _store.SetAsync("App:Timeout", "30");
        await _store.SetAsync("development/App:Timeout", "999");

        var env = EnvironmentProfile.From("Development");
        using var provider = new PragmaticConfigurationProvider(_store, env, keyPrefix: null);

        provider.Load();

        provider.TryGet("App:Timeout", out var timeout).Should().BeTrue();
        timeout.Should().Be("999");
    }

    [Fact]
    public async Task Load_EnvironmentOverlay_BaseValuePreservedWhenNoOverride()
    {
        await _store.SetAsync("App:Timeout", "30");
        await _store.SetAsync("App:Name", "MyApp");
        await _store.SetAsync("development/App:Timeout", "999");

        var env = EnvironmentProfile.From("Development");
        using var provider = new PragmaticConfigurationProvider(_store, env, keyPrefix: null);

        provider.Load();

        // Name has no dev override — keeps base value
        provider.TryGet("App:Name", out var name).Should().BeTrue();
        name.Should().Be("MyApp");
    }

    [Fact]
    public async Task LoadAsync_ReloadsData()
    {
        await _store.SetAsync("App:Timeout", "30");

        var env = EnvironmentProfile.From("Production");
        using var provider = new PragmaticConfigurationProvider(_store, env, keyPrefix: null);

        provider.Load();
        provider.TryGet("App:Timeout", out var v1);
        v1.Should().Be("30");

        // Update value in store
        await _store.SetAsync("App:Timeout", "60");

        // Reload
        await provider.LoadAsync(CancellationToken.None);

        provider.TryGet("App:Timeout", out var v2);
        v2.Should().Be("60");
    }
}
