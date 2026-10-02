using Testcontainers.Redis;
using Xunit;

namespace Pragmatic.Caching.Redis.Tests;

/// <summary>One Redis for the suite. Without Docker it fails at start, loudly: a skipped suite reads as a green one.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder().Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
