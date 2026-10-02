using Pragmatic.Testing.Assertions;
using Pragmatic.FeatureFlags.Providers;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests.Providers;

/// <summary>
///     Verifies that flag changes are <b>broadcast</b>: every concurrent watcher observes every change.
///     A single shared channel made watchers compete for messages, so two components watching the same
///     store each saw an arbitrary subset — the kind of split that only shows up under load.
/// </summary>
public class FeatureFlagChangeBroadcastTests
{
    private static async Task<List<FeatureFlagChange>> CollectAsync(
        IAsyncEnumerable<FeatureFlagChange> stream, int expected, CancellationToken ct)
    {
        var collected = new List<FeatureFlagChange>();
        await foreach (var change in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            collected.Add(change);
            if (collected.Count == expected)
                break;
        }

        return collected;
    }

    [Fact]
    public async Task TwoWatchers_BothObserveEveryChange()
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition { Name = "flag", Enabled = true });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Start both watchers before publishing, then let them settle so neither misses the burst.
        var first = CollectAsync(store.WatchAsync(cts.Token), 3, cts.Token);
        var second = CollectAsync(store.WatchAsync(cts.Token), 3, cts.Token);
        await Task.Delay(100, cts.Token);

        store.Define(new FeatureFlagDefinition { Name = "flag", Enabled = false });
        store.Define(new FeatureFlagDefinition { Name = "flag", Enabled = true });
        store.Define(new FeatureFlagDefinition { Name = "flag", Enabled = false });

        var firstChanges = await first;
        var secondChanges = await second;

        firstChanges.Should().HaveCount(3, "a watcher must observe every change, not a share of them");
        secondChanges.Should().HaveCount(3);
        firstChanges.Select(c => c.IsEnabled).Should().Equal(secondChanges.Select(c => c.IsEnabled));
    }

    [Fact]
    public async Task ChangePublishedBeforeAnyWatcher_IsDeliveredToTheFirstWatcher()
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition { Name = "seeded", Enabled = true });
        store.Define(new FeatureFlagDefinition { Name = "seeded", Enabled = false });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var changes = await CollectAsync(store.WatchAsync(cts.Token), 1, cts.Token);

        changes.Should().ContainSingle()
            .Which.FlagName.Should().Be("seeded",
                "a change published while nobody was watching is held for the first watcher");
    }

    [Fact]
    public async Task WatcherThatStopped_DoesNotBlockRemainingWatchers()
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition { Name = "flag", Enabled = true });

        using var shortLived = new CancellationTokenSource();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var survivor = CollectAsync(store.WatchAsync(cts.Token), 1, cts.Token);
        var abandoned = CollectAsync(store.WatchAsync(shortLived.Token), 1, shortLived.Token);
        await Task.Delay(100, cts.Token);

        await shortLived.CancelAsync();
        await Task.Delay(100, cts.Token);
        try { await abandoned; } catch (OperationCanceledException) { /* expected */ }

        store.Define(new FeatureFlagDefinition { Name = "flag", Enabled = false });

        (await survivor).Should().ContainSingle();
    }

    [Fact]
    public async Task DisposedConfigurationStore_CompletesItsWatchers()
    {
        var store = new global::Pragmatic.FeatureFlags.Configuration.ConfigurationFeatureFlagStore(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watching = CollectAsync(store.WatchAsync(cts.Token), int.MaxValue, cts.Token);
        await Task.Delay(100, cts.Token);

        store.Dispose();

        // Completing the stream ends the enumeration instead of hanging until the token fires.
        (await watching).Should().BeEmpty();
    }
}
