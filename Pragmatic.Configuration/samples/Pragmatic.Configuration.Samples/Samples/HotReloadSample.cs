using Pragmatic.Configuration.Providers;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     Hot-reload via <see cref="IConfigurationStore.WatchAsync"/>: a background watcher
///     streams <see cref="ConfigurationChange"/> notifications as keys matching a pattern
///     are written. This is the change feed that the configuration bridge consumes to drive
///     <c>IOptionsMonitor&lt;T&gt;</c> reload callbacks.
/// </summary>
public static class HotReloadSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("2. Hot-Reload — WatchAsync change stream");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        IConfigurationStore store = new InMemoryConfigurationStore();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = new List<ConfigurationChange>();

        // Watch every key under the "feature:" prefix. The watcher runs until we cancel it.
        var watcher = Task.Run(async () =>
        {
            try
            {
                await foreach (var change in store.WatchAsync("feature:*", cts.Token))
                {
                    received.Add(change);
                    Console.WriteLine(
                        $"    [watch] {change.Key}: '{change.OldValue ?? "(none)"}' -> '{change.NewValue ?? "(deleted)"}'");

                    // Demo stops after observing the three writes below.
                    if (received.Count == 3)
                        await cts.CancelAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the demo window closes.
            }
        }, cts.Token);

        // Give the watcher a moment to subscribe before producing changes.
        await Task.Delay(50, CancellationToken.None);

        Console.WriteLine("  Producing configuration changes:");
        await store.SetAsync("feature:dark-mode", "true");
        await store.SetAsync("feature:beta-api", "false");
        await store.SetAsync("feature:beta-api", "true");   // update — fires again
        await store.SetAsync("other:ignored", "x");          // outside pattern — not observed

        await watcher;

        Console.WriteLine();
        Console.WriteLine($"  Observed {received.Count} change(s) for 'feature:*' (the 'other:' write was filtered out).");
        Console.WriteLine();
    }
}
