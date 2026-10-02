using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pragmatic.Persistence.EFCore.Repository;

/// <summary>
///     Hosted service that preloads all <see cref="ILookupCacheLoader"/> caches at application startup.
///     Discovers loaders via DI multi-registration and calls <see cref="ILookupCacheLoader.LoadAsync"/> sequentially.
/// </summary>
public sealed class LookupPreloadHostedService(
    IServiceProvider serviceProvider,
    IEnumerable<ILookupCacheLoader> loaders,
    ILogger<LookupPreloadHostedService>? logger = null) : IHostedService
{
    private readonly ILogger _logger = logger ?? NullLogger<LookupPreloadHostedService>.Instance;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var loader in loaders)
        {
            try
            {
                await loader.LoadAsync(serviceProvider, cancellationToken).ConfigureAwait(false);
                count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fail fast: swallowing the error would leave the cache empty, which then throws a
                // KeyNotFoundException from every non-nullable lookup navigation at request time —
                // a startup misconfiguration disguised as scattered runtime failures. Surface
                // it at startup so the app does not run with an incomplete lookup cache.
                _logger.LogError(ex, "Lookup cache loader {LoaderType} failed during startup",
                    loader.GetType().Name);
                throw new InvalidOperationException(
                    $"Lookup cache loader '{loader.GetType().Name}' failed at startup; the application " +
                    "cannot start with an incomplete lookup cache. See the inner exception.", ex);
            }
        }

        if (count > 0)
            _logger.LogInformation("Preloaded {Count} lookup cache(s)", count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
