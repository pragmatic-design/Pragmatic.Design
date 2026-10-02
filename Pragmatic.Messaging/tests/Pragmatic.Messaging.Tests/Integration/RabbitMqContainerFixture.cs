using Testcontainers.RabbitMq;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Starts one RabbitMQ container for the test collection (Testcontainers), so the broker
///     integration tests actually run in CI instead of silently skipping. When Docker is not
///     available the fixture degrades gracefully: <see cref="ConnectionString"/> stays null and
///     tests keep the legacy skip behavior.
/// </summary>
public sealed class RabbitMqContainerFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    /// <summary>AMQP connection string, or null when Docker is unavailable.</summary>
    public string? ConnectionString { get; private set; }

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
    public async Task InitializeAsync()
    {
        try
        {
            _container = new RabbitMqBuilder().Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch
        {
            // Docker not available — tests skip gracefully.
            ConnectionString = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
#pragma warning restore CA2007
}
