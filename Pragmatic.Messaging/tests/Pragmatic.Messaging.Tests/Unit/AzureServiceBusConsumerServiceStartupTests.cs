using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.AzureServiceBus;
using Pragmatic.Messaging.Routing;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     The transport is connected by the time the host reports started.
/// </summary>
/// <remarks>
///     On .NET 10 a BackgroundService runs all of ExecuteAsync in the background: work before its first
///     await has not run when StartAsync returns. A consumer service that connected there would leave a
///     publish issued as soon as the application starts finding the transport Disconnected — a race the
///     thread pool wins on one machine and loses on another.
/// </remarks>
public class AzureServiceBusConsumerServiceStartupTests
{
    // The emulator's development connection string. ServiceBusClient accepts it with no broker running:
    // creating the client opens no connection.
    private const string OfflineConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    [Fact]
    public async Task TheTransportIsConnected_WhenStartAsyncReturns()
    {
        var transport = new AzureServiceBusTransport(
            new AzureServiceBusOptions { ConnectionString = OfflineConnectionString, AutoCreateEntities = false },
            NullLogger<AzureServiceBusTransport>.Instance);
        var services = new ServiceCollection().BuildServiceProvider();
        var consumer = new AzureServiceBusConsumerService(
            transport,
            services.GetRequiredService<IServiceScopeFactory>(),
            new DefaultMessageRouter(),
            [],
            [],
            NullLogger<AzureServiceBusConsumerService>.Instance);

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
