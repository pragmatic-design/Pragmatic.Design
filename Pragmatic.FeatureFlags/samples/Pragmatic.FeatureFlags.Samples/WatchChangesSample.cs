using Pragmatic.FeatureFlags;
using Pragmatic.FeatureFlags.Providers;

namespace Pragmatic.FeatureFlags.Samples;

/// <summary>
/// Demonstrates <see cref="IFeatureFlagStore.WatchAsync"/>: a long-lived consumer
/// (typically a <c>BackgroundService</c>) reacting to flag changes to invalidate
/// caches or refresh local state.
///
/// <para>
/// The in-memory store emits a <see cref="FeatureFlagChange"/> whenever an existing
/// flag definition is re-<c>Define</c>d with different content (Enabled, Description,
/// or Rules). The change carries the before/after Enabled state.
/// </para>
/// </summary>
public static class WatchChangesSample
{
    public static async Task RunAsync()
    {
        var store = new InMemoryFeatureFlagStore();

        // The store's change stream is unbounded and never auto-completes, so we bound
        // the consumer with a cancellation token (a BackgroundService would use its
        // stopping token instead).
        using var cts = new CancellationTokenSource();

        var observed = new List<string>();
        var consumer = ConsumeChangesAsync(store, observed, cts.Token);

        // First Define adds the flag (no change event — there was nothing before).
        store.Define(new FeatureFlagDefinition { Name = "new-checkout", Enabled = false });

        // Re-Define with a flipped Enabled value -> emits a change.
        store.Define(new FeatureFlagDefinition { Name = "new-checkout", Enabled = true });

        // Re-Define with the same content -> no change event.
        store.Define(new FeatureFlagDefinition { Name = "new-checkout", Enabled = true });

        // Re-Define adding a rule -> content changed -> emits a change.
        store.Define(new FeatureFlagDefinition
        {
            Name = "new-checkout",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "percentage", Values = ["50"], Enabled = true }],
        });

        // Give the consumer a moment to drain the channel, then stop it.
        await Task.Delay(50, CancellationToken.None);
        await cts.CancelAsync();
        try
        {
            await consumer;
        }
        catch (OperationCanceledException)
        {
            // Expected: cancelling the token ends the await-foreach loop.
        }

        Console.WriteLine($"  observed {observed.Count} change notification(s):");
        foreach (var line in observed)
        {
            Console.WriteLine($"    {line}");
        }
    }

    private static async Task ConsumeChangesAsync(InMemoryFeatureFlagStore store, List<string> sink, CancellationToken ct)
    {
        await foreach (var change in store.WatchAsync(ct))
        {
            sink.Add($"{change.FlagName}: {change.WasEnabled} -> {change.IsEnabled}");
        }
    }
}
