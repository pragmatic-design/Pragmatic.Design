using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pragmatic.Configuration.Bridge;

/// <summary>
///     ConfigurationProvider that loads values from <see cref="IConfigurationStore"/>
///     and subscribes to <see cref="IConfigurationStore.WatchAsync"/> for hot-reload.
///     When a change is detected, the provider reloads and triggers
///     <see cref="ConfigurationProvider.OnReload"/> so that IOptionsMonitor{T} picks up changes.
/// </summary>
internal sealed partial class PragmaticConfigurationProvider(
    IConfigurationStore store,
    EnvironmentProfile environment,
    string? keyPrefix,
    ILogger<PragmaticConfigurationProvider>? logger = null)
    : ConfigurationProvider, IDisposable
{
    private readonly ILogger<PragmaticConfigurationProvider> _logger = logger ?? NullLogger<PragmaticConfigurationProvider>.Instance;
    private CancellationTokenSource? _watchCts;
    private readonly Lock _watchLock = new();

    /// <summary>
    ///     Loads configuration from the store synchronously (called by the framework on startup).
    ///     Also starts the background watcher for hot-reload.
    /// </summary>
    /// <remarks>
    ///     Sync-over-async is acceptable here because <see cref="ConfigurationProvider.Load"/> has no async
    ///     override in Microsoft.Extensions.Configuration, and it is called during host build phase
    ///     on a single thread before any SynchronizationContext is established.
    /// </remarks>
    public override void Load()
    {
        LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Start watching for changes (idempotent — only starts once)
        StartWatcher();
    }

    /// <summary>
    ///     Loads all configuration values from the store, applying cascade resolution.
    /// </summary>
    internal async Task LoadAsync(CancellationToken ct)
    {
        var prefix = keyPrefix ?? "";
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Environment-overlay keys are stored as "{env}/{key}". When the base prefix is empty (or short),
        // GetSectionAsync returns those overlay keys too; collect their prefixes so the base load can
        // skip them — otherwise they'd leak into config as literal "env:..." entries alongside the
        // properly-resolved values applied by the overlay loop below.
        var envPrefixes = new List<string>();
        for (var i = 1; i < environment.ResolutionChain.Count; i++)
            envPrefixes.Add($"{environment.ResolutionChain[i]}/");

        // Load base values
        var baseValues = await store.GetSectionAsync(prefix, ct).ConfigureAwait(false);
        foreach (var kv in baseValues)
        {
            if (envPrefixes.Exists(p => kv.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;
            data[NormalizeKey(kv.Key)] = kv.Value;
        }

        // Load environment overlays (most general → most specific, last wins)
        for (var i = 1; i < environment.ResolutionChain.Count; i++)
        {
            var envPrefix = $"{environment.ResolutionChain[i]}/{prefix}";
            var envValues = await store.GetSectionAsync(envPrefix, ct).ConfigureAwait(false);
            foreach (var kv in envValues)
            {
                // Strip the environment prefix to get the original key.
                // Guard: store may return keys that don't start with envPrefix (e.g. partial prefix match).
                if (kv.Key.Length <= envPrefix.Length)
                    continue;
                var originalKey = kv.Key[envPrefix.Length..];
                data[NormalizeKey($"{prefix}{originalKey}")] = kv.Value;
            }
        }

        // Assign under a lock so concurrent reads (via TryGet on the base class)
        // don't observe a partially-replaced dictionary.
        lock (_watchLock)
        {
            Data = data;
        }
    }

    private void StartWatcher()
    {
        CancellationToken ct;
        lock (_watchLock)
        {
            if (_watchCts is not null)
                return;

            _watchCts = new CancellationTokenSource();
            ct = _watchCts.Token;
        }

        var watchPattern = string.IsNullOrEmpty(keyPrefix) ? "*" : $"{keyPrefix}*";

        // Runs in the background with proper exception handling
        _ = WatchForChangesAsync(watchPattern, ct);
    }

    private async Task WatchForChangesAsync(string pattern, CancellationToken ct)
    {
        try
        {
            await foreach (var change in store.WatchAsync(pattern, ct).ConfigureAwait(false))
            {
                try
                {
                    // Reload all data from the store
                    await LoadAsync(ct).ConfigureAwait(false);
                    OnReload();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Log reload failure but keep watching for subsequent changes.
                    // Throwing here would kill the watcher loop entirely.
                    LogReloadFailed(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose
        }
        catch (Exception ex)
        {
            // Unexpected failure in the watch stream itself — log and stop watching.
            // Without this, the exception is silently swallowed by the fire-and-forget task.
            LogWatcherFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Pragmatic configuration reload failed — watcher continues, but stale values may be served until the next successful reload")]
    private partial void LogReloadFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Pragmatic configuration watcher terminated unexpectedly — hot-reload is now disabled for this provider")]
    private partial void LogWatcherFailed(Exception ex);

    /// <summary>
    ///     Normalizes keys to the Microsoft.Extensions.Configuration format.
    ///     Replaces "/" with ":" for consistency.
    /// </summary>
    private static string NormalizeKey(string key)
        => key.Replace('/', ':');

    public void Dispose()
    {
        lock (_watchLock)
        {
            _watchCts?.Cancel();
            _watchCts?.Dispose();
            _watchCts = null;
        }
    }
}
