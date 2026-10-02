using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Kafka;
using Pragmatic.Messaging.Routing;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     The Kafka transport is connected by the time the host reports started.
/// </summary>
/// <remarks>
///     The same race <see cref="AzureServiceBusConsumerServiceStartupTests" /> describes: on .NET 10 all of
///     <c>ExecuteAsync</c> runs in the background, so a connect made there would not have happened when the
///     host finishes starting. Connecting Kafka builds the producer and opens no connection, so it moves to
///     <c>StartAsync</c> at no cost to startup.
/// </remarks>
public class KafkaConsumerServiceStartupTests
{
    [Fact]
    public async Task TheTransportIsConnected_WhenStartAsyncReturns()
    {
        // No broker listens here: building the producer does not reach it.
        var transport = new KafkaTransport(
            new KafkaOptions { BootstrapServers = "127.0.0.1:1", AutoCreateTopics = false },
            NullLogger<KafkaTransport>.Instance);
        var services = new ServiceCollection().BuildServiceProvider();
        var consumer = new KafkaConsumerService(
            transport,
            services.GetRequiredService<IServiceScopeFactory>(),
            new DefaultMessageRouter(),
            [],
            [],
            NullLogger<KafkaConsumerService>.Instance);

        try
        {
            await consumer.StartAsync(CancellationToken.None).ConfigureAwait(true);

            transport.Status.Should().Be(TransportStatus.Connected);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None).ConfigureAwait(true);
            await transport.DisposeAsync().ConfigureAwait(true);
            await services.DisposeAsync().ConfigureAwait(true);
        }
    }
}
