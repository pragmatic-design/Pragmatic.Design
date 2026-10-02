using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Caching.Extensions;

namespace Pragmatic.Caching.Redis.Tests;

/// <summary>
///     One node: its own container, its own in-memory <c>HybridCache</c> and no distributed cache
///     behind it — so the only thing two nodes share is the Redis channel, when they have one.
/// </summary>
/// <remarks>
///     No L2 on purpose. What the broadcast promises is about each node's own copy; with a shared L2
///     a second node could be served the first one's entry and the test would be measuring Redis as a
///     cache instead of Redis as a messenger.
/// </remarks>
public sealed class CacheNode : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly IHostedService[] _hosted;

    private CacheNode(ServiceProvider services, IHostedService[] hosted)
    {
        _services = services;
        _hosted = hosted;
    }

    /// <summary>The stack an application would use, decorated when the node has the broadcast.</summary>
    public ICacheStack Stack => _services.GetRequiredService<ICacheStack>();

    /// <summary>This node as it signs and receives broadcasts, or null without the broadcast.</summary>
    public string? NodeId => _services.GetService<RedisCacheInvalidationChannel>()?.NodeId;

    /// <param name="redis">The broadcast's Redis, or null for a node that has none.</param>
    public static async Task<CacheNode> StartAsync(string? redis)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        services.AddPragmaticCaching();
        if (redis is not null)
            services.AddRedisCacheInvalidationBroadcast(redis);

        var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToArray();
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);

        return new CacheNode(provider, hosted);
    }

    /// <summary>Reads the key through the stack, and says whether the value had to be produced.</summary>
    public async Task<bool> ProducesAsync(string key, string tag)
    {
        var produced = false;
        await Stack.GetOrSetAsync(key, _ =>
        {
            produced = true;
            return ValueTask.FromResult(key);
        }, new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] });

        return produced;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var service in _hosted)
            await service.StopAsync(CancellationToken.None);

        await _services.DisposeAsync();
    }
}
