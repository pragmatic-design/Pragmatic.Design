using Testcontainers.Redis;
using Xunit;

namespace Pragmatic.Configuration.Redis.Tests;

/// <summary>Redis container. Skips gracefully when Docker is unavailable.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    /// <summary>Redis connection string, or <c>null</c> when Docker is unavailable (tests skip).</summary>
    public string? ConnectionString { get; private set; }

    public string? StartupError { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new RedisBuilder().Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception ex)
        {
            ConnectionString = null;
            StartupError = ex.ToString();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
