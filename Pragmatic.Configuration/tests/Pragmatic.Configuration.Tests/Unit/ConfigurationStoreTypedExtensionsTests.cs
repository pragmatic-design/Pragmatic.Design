using System;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration;
using Pragmatic.Configuration.Providers;
using Xunit;

namespace Pragmatic.Configuration.Tests.Unit;

/// <summary>Verifies the typed get/set convenience over <see cref="IConfigurationStore" />.</summary>
public class ConfigurationStoreTypedExtensionsTests
{
    private enum Mode { Off, On }

    [Fact]
    public async Task SetValue_GetValue_RoundTripsCommonTypes()
    {
        var store = new InMemoryConfigurationStore();

        await store.SetValueAsync("Timeout", 30);
        await store.SetValueAsync("Enabled", true);
        await store.SetValueAsync("Rate", 1.5);
        await store.SetValueAsync("Mode", Mode.On);
        var id = Guid.NewGuid();
        await store.SetValueAsync("Id", id);

        (await store.GetValueAsync<int>("Timeout")).Should().Be(30);
        (await store.GetValueAsync<bool>("Enabled")).Should().BeTrue();
        (await store.GetValueAsync<double>("Rate")).Should().Be(1.5);
        (await store.GetValueAsync<Mode>("Mode")).Should().Be(Mode.On);
        (await store.GetValueAsync<Guid>("Id")).Should().Be(id);
    }

    [Fact]
    public async Task GetValue_UnsetKey_ReturnsDefault()
    {
        var store = new InMemoryConfigurationStore();

        (await store.GetValueAsync<int>("missing")).Should().Be(0);
        (await store.GetValueAsync("missing", defaultValue: 42)).Should().Be(42);
    }

    [Fact]
    public async Task GetValue_InvariantCulture_ForNumbers()
    {
        var store = new InMemoryConfigurationStore();

        await store.SetValueAsync("Rate", 1234.56m);
        var raw = await store.GetAsync("Rate");

        raw.Should().Be("1234.56", "numbers are formatted with the invariant culture");
        (await store.GetValueAsync<decimal>("Rate")).Should().Be(1234.56m);
    }
}
