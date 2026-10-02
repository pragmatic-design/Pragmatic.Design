using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration;
using Xunit;

namespace Pragmatic.Configuration.Aws.Tests;

/// <summary>End-to-end configuration roundtrip against LocalStack's AWS SSM Parameter Store.</summary>
public sealed class AwsParameterStoreTests(LocalStackFixture fixture) : IClassFixture<LocalStackFixture>
{
    private IConfigurationStore Build()
    {
        var services = new ServiceCollection();
        services.AddAwsParameterStore(o =>
        {
            o.ServiceUrl = fixture.ServiceUrl!;
            o.Region = LocalStackFixture.Region;
            o.AccessKey = LocalStackFixture.AccessKey;
            o.SecretKey = LocalStackFixture.SecretKey;
            o.PathPrefix = "pragmatic";
        });
        services.AddLogging();

        return services.BuildServiceProvider().GetRequiredService<IConfigurationStore>();
    }

    [Fact]
    public async Task Set_Get_Delete_RoundTrips()
    {
        if (fixture.ServiceUrl is null)
            return;

        var store = Build();

        (await store.GetAsync("Booking:CancellationWindowHours")).Should().BeNull();

        await store.SetAsync("Booking:CancellationWindowHours", "24");
        (await store.GetAsync("Booking:CancellationWindowHours")).Should().Be("24");

        await store.DeleteAsync("Booking:CancellationWindowHours");
        (await store.GetAsync("Booking:CancellationWindowHours")).Should().BeNull();
    }

    [Fact]
    public async Task GetSection_ReturnsMatchingKeys_InLogicalForm()
    {
        if (fixture.ServiceUrl is null)
            return;

        var store = Build();

        await store.SetAsync("Booking:Database:Host", "localhost");
        await store.SetAsync("Booking:Database:Port", "5432");
        await store.SetAsync("Payment:ApiUrl", "https://pay");

        var section = await store.GetSectionAsync("Booking:Database:");

        section.Should().HaveCount(2);
        section["Booking:Database:Host"].Should().Be("localhost");
        section["Booking:Database:Port"].Should().Be("5432");
    }

    [Fact]
    public async Task TenantOverride_IsIsolatedFromBase()
    {
        if (fixture.ServiceUrl is null)
            return;

        var store = Build();

        await store.SetAsync("Theme", "default");
        await store.SetAsync("Theme", "dark", "tenant-A");

        (await store.GetAsync("Theme")).Should().Be("default");
        (await store.GetAsync("Theme", "tenant-A")).Should().Be("dark");
        (await store.GetAsync("Theme", "tenant-B")).Should().BeNull();
    }

    [Fact]
    public async Task WatchAsync_YieldsNothing_NoNativeChangeStream()
    {
        if (fixture.ServiceUrl is null)
            return;

        var store = Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var count = 0;
        try
        {
            await foreach (var _ in store.WatchAsync("*", cts.Token))
                count++;
        }
        catch (OperationCanceledException)
        {
        }

        count.Should().Be(0, "Parameter Store exposes no native change stream");
    }
}
