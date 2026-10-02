using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Background service that watches the configuration store and dispatches each change to the typed
///     <c>IConfigurationChangeHandler&lt;TOptions&gt;</c> whose section matches — turning the raw change
///     stream into per-option callbacks. Handlers run in a fresh DI scope; a throwing handler is logged and
///     never breaks the watch loop.
/// </summary>
internal sealed partial class ConfigurationChangeDispatcher(
    IConfigurationStore store,
    IEnumerable<ConfigurationChangeSubscription> subscriptions,
    IServiceScopeFactory scopeFactory,
    ILogger<ConfigurationChangeDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subs = subscriptions.ToArray();
        if (subs.Length == 0)
            return; // No typed handlers registered — nothing to watch.

        try
        {
            await foreach (var change in store.WatchAsync("*", stoppingToken).ConfigureAwait(false))
            {
                var logicalKey = LogicalKey(change.Key);

                foreach (var sub in subs)
                {
                    if (!Matches(logicalKey, sub.Section))
                        continue;

                    using var scope = scopeFactory.CreateScope();
                    try
                    {
                        await sub.Invoke(scope.ServiceProvider, change, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogHandlerFailed(sub.Section, change.Key, ex);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>Strips an environment-overlay prefix so "staging/Booking:X" matches section "Booking".</summary>
    private static string LogicalKey(string key)
    {
        var slash = key.LastIndexOf('/');
        return slash >= 0 ? key[(slash + 1)..] : key;
    }

    private static bool Matches(string logicalKey, string section)
        => logicalKey.Equals(section, StringComparison.Ordinal)
           || logicalKey.StartsWith(section + ":", StringComparison.Ordinal);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Configuration change handler for section {Section} failed handling key {Key}")]
    private partial void LogHandlerFailed(string section, string key, Exception ex);
}
