using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration;
using Xunit;

namespace Pragmatic.Configuration.Redis.Tests;

/// <summary>End-to-end configuration roundtrip against a real Redis instance.</summary>
public sealed class RedisConfigurationStoreTests(RedisFixture fixture) : IClassFixture<RedisFixture>
{
    private IConfigurationStore Build()
    {
        var services = new ServiceCollection();
        services.AddRedisConfigurationStore(o =>
        {
            o.Configuration = fixture.ConnectionString!;
            o.KeyPrefix = "pragmatic";
        });
        services.AddLogging();
        return services.BuildServiceProvider().GetRequiredService<IConfigurationStore>();
    }

    [Fact]
    public async Task Set_Get_Delete_RoundTrips()
    {
        if (fixture.ConnectionString is null)
            return;

        var store = Build();

        (await store.GetAsync("Booking:Window")).Should().BeNull();

        await store.SetAsync("Booking:Window", "24");
        (await store.GetAsync("Booking:Window")).Should().Be("24");

        await store.SetAsync("Booking:Window", "48");
        (await store.GetAsync("Booking:Window")).Should().Be("48");

        await store.DeleteAsync("Booking:Window");
        (await store.GetAsync("Booking:Window")).Should().BeNull();
    }

    [Fact]
    public async Task GetSection_ReturnsMatchingKeys_InLogicalForm()
    {
        if (fixture.ConnectionString is null)
            return;

        var store = Build();

        await store.SetAsync("Cache:Ttl", "300");
        await store.SetAsync("Cache:Enabled", "true");
        await store.SetAsync("Other:Key", "x");

        var section = await store.GetSectionAsync("Cache:");

        section.Should().HaveCount(2);
        section["Cache:Ttl"].Should().Be("300");
        section["Cache:Enabled"].Should().Be("true");
    }

    [Fact]
    public async Task TenantOverride_IsIsolatedFromBase()
    {
        if (fixture.ConnectionString is null)
            return;

        var store = Build();

        await store.SetAsync("Theme", "default");
        await store.SetAsync("Theme", "dark", "tenant-A");

        (await store.GetAsync("Theme")).Should().Be("default");
        (await store.GetAsync("Theme", "tenant-A")).Should().Be("dark");
        (await store.GetAsync("Theme", "tenant-B")).Should().BeNull();
    }
}
