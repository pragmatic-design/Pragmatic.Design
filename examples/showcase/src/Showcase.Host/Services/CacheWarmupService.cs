
namespace Showcase.Host.Services;

/// <summary>
/// Warms up caches on application startup.
/// Demonstrates: [Service(Lifetime = Singleton)] — long-lived, shared instance.
/// </summary>
[Service(Lifetime = Lifetime.Singleton, AsSelf = true)]
public class CacheWarmupService(ILogger<CacheWarmupService> logger)
{
    private bool _warmedUp;

    public Task WarmUpAsync(CancellationToken ct = default)
    {
        if (_warmedUp)
            return Task.CompletedTask;

        logger.LogInformation("Warming up caches...");
        _warmedUp = true;
        logger.LogInformation("Cache warmup completed.");

        return Task.CompletedTask;
    }
}
