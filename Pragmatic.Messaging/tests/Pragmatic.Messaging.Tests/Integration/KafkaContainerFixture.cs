using Testcontainers.Kafka;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Starts one Kafka container for the test collection (Testcontainers), so the broker
///     integration tests actually run in CI instead of silently skipping. When Docker is not
///     available the fixture degrades gracefully: <see cref="BootstrapServers"/> stays null and
///     tests keep the legacy skip behavior.
/// </summary>
public sealed class KafkaContainerFixture : IAsyncLifetime
{
    private KafkaContainer? _container;

    /// <summary>Bootstrap servers, or null when Docker is unavailable.</summary>
    public string? BootstrapServers { get; private set; }

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
    public async Task InitializeAsync()
    {
        try
        {
            _container = new KafkaBuilder().Build();
            await _container.StartAsync();
            BootstrapServers = _container.GetBootstrapAddress();
        }
        catch
        {
            // Docker not available — tests skip gracefully.
            BootstrapServers = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
#pragma warning restore CA2007
}
