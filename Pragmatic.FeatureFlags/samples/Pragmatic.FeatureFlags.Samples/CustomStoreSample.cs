using Microsoft.Extensions.DependencyInjection;
using Pragmatic.FeatureFlags;

namespace Pragmatic.FeatureFlags.Samples;

/// <summary>
/// Demonstrates registering a custom <see cref="IFeatureFlagStore"/> via the generic
/// <c>AddPragmaticFeatureFlags&lt;TStore&gt;()</c> overload. Use this to back flags
/// with a database, a remote service, or — as here — a hard-coded provider.
/// </summary>
public static class CustomStoreSample
{
    /// <summary>A trivial read-only store with two baked-in flags.</summary>
    private sealed class HardCodedFeatureFlagStore : IFeatureFlagStore
    {
        private static readonly Dictionary<string, bool> Flags = new(StringComparer.OrdinalIgnoreCase)
        {
            ["maintenance-banner"] = true,
            ["dark-mode"] = false,
        };

        public Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default)
            => Task.FromResult(Flags.TryGetValue(flagName, out var enabled) && enabled);

        public Task<bool> IsEnabledAsync(string flagName, FeatureFlagContext context, CancellationToken ct = default)
            => IsEnabledAsync(flagName, ct);

        public Task<FeatureFlagDefinition?> GetDefinitionAsync(string flagName, CancellationToken ct = default)
        {
            FeatureFlagDefinition? definition = Flags.TryGetValue(flagName, out var enabled)
                ? new FeatureFlagDefinition { Name = flagName, Enabled = enabled }
                : null;
            return Task.FromResult(definition);
        }

        public Task<IReadOnlyList<FeatureFlagDefinition>> GetAllAsync(CancellationToken ct = default)
        {
            IReadOnlyList<FeatureFlagDefinition> all =
                [.. Flags.Select(kv => new FeatureFlagDefinition { Name = kv.Key, Enabled = kv.Value })];
            return Task.FromResult(all);
        }

        public async IAsyncEnumerable<FeatureFlagChange> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            // Static store: nothing ever changes, so the stream completes immediately.
            await Task.CompletedTask;
            yield break;
        }
    }

    public static async Task RunAsync()
    {
        var services = new ServiceCollection();
        services.AddPragmaticFeatureFlags<HardCodedFeatureFlagStore>();

        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IFeatureFlagStore>();

        Console.WriteLine($"  resolved store type: {store.GetType().Name}");
        Console.WriteLine($"  maintenance-banner -> {await store.IsEnabledAsync("maintenance-banner")}");
        Console.WriteLine($"  dark-mode          -> {await store.IsEnabledAsync("dark-mode")}");

        var all = await store.GetAllAsync();
        Console.WriteLine($"  GetAllAsync() returned {all.Count} flags.");
    }
}
