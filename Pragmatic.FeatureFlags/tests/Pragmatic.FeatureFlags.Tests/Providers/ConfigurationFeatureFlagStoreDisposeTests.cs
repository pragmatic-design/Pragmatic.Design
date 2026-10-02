using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Configuration;
using Pragmatic.FeatureFlags.Configuration;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests.Providers;

public class ConfigurationFeatureFlagStoreDisposeTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task Dispose_CompletesActiveWatchStream()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["FeatureFlags:Live:Enabled"] = "true",
        });
        var store = new ConfigurationFeatureFlagStore(config);

        var enumerator = store.WatchAsync().GetAsyncEnumerator();

        // Start the first read so the watcher channel registers, then dispose.
        var moveNext = enumerator.MoveNextAsync();

        store.Dispose();

        // TryComplete on Dispose ends the stream via completion, not cancellation.
        var hasNext = await moveNext;
        hasNext.Should().BeFalse();

        await enumerator.DisposeAsync();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["FeatureFlags:Live:Enabled"] = "true",
        });
        var store = new ConfigurationFeatureFlagStore(config);

        var act = () =>
        {
            store.Dispose();
            store.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task IsEnabledAsync_AfterDispose_StillReadsLastLoadedCache()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["FeatureFlags:Cached:Enabled"] = "true",
        });
        var store = new ConfigurationFeatureFlagStore(config);

        store.Dispose();

        // Dispose only tears down the reload subscription and watchers; the cached
        // definitions remain readable.
        var result = await store.IsEnabledAsync("Cached");
        result.Should().BeTrue();
    }
}
