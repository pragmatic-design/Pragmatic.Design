using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Messaging.Routing;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     A transport whose connect fails while the host starts is retried in the background, and never
///     faults the consumer service: under the host's default <c>StopHost</c>, a faulted background service
///     stops the whole application, which an application that starts with its broker down must not do.
/// </summary>
public class ABrokerDownAtStartTests
{
    private static readonly ConnectRetry Quick = new(TimeSpan.FromMilliseconds(10), 0);

    [Fact]
    public async Task AConnectThatFailsAtStart_IsRetried_AndTheServiceRunsOnInsteadOfFaulting()
    {
        var transport = new ATransportThatConnectsAtAttempt(3);
        var services = new ServiceCollection().BuildServiceProvider();
        var consumer = new NamedBusConsumerService(
            "orders", transport, services.GetRequiredService<IServiceScopeFactory>(), new DefaultMessageRouter(), [],
            NullLogger<NamedBusConsumerService>.Instance, retry: Quick);

        try
        {
            await consumer.StartAsync(CancellationToken.None).ConfigureAwait(true);
            await transport.Connected.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            transport.Attempts.Should().Be(3);
            consumer.ExecuteTask!.IsFaulted.Should().BeFalse("a failed connect is retried, not thrown at the host");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None).ConfigureAwait(true);
            await services.DisposeAsync().ConfigureAwait(true);
        }

        consumer.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task TheRetryGivesUpAfterItsAttempts_WithoutThrowing()
    {
        var transport = new ATransportThatConnectsAtAttempt(int.MaxValue);

        var connected = await TransportConnectLoop.UntilConnectedAsync(
            transport, transport.ConnectAsync(), Quick with { MaxAttempts = 3 }, NullLogger.Instance, CancellationToken.None)
            .ConfigureAwait(true);

        connected.Should().BeFalse();
        transport.Attempts.Should().Be(3, "MaxAttempts counts the first attempt too");
    }

    [Fact]
    public async Task AHostThatStopsWhileTheRetryWaits_EndsItAsACancellation()
    {
        var transport = new ATransportThatConnectsAtAttempt(int.MaxValue);
        using var stopping = new CancellationTokenSource();
        var slow = new ConnectRetry(TimeSpan.FromMinutes(1), 0);

        var retrying = TransportConnectLoop.UntilConnectedAsync(
            transport, transport.ConnectAsync(), slow, NullLogger.Instance, stopping.Token);
        await stopping.CancelAsync().ConfigureAwait(true);

        var waited = () => retrying;
        await waited.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
        transport.Attempts.Should().Be(1, "the host stopped during the wait, before a second attempt");
    }

    /// <summary>
    ///     The RabbitMQ service reads its retry from <see cref="RabbitMqOptions" />: against a closed port it
    ///     gives up after <see cref="RabbitMqOptions.MaxReconnectAttempts" /> attempts and runs on. With no
    ///     limit read, it would retry for ever and the wait below would time out.
    /// </summary>
    [Fact]
    public async Task RabbitMq_BrokerUnreachableAtStart_GivesUpAfterMaxReconnectAttempts_WithoutFaulting()
    {
        var options = new RabbitMqOptions
        {
            ConnectionString = "amqp://guest:guest@127.0.0.1:1/",
            ReconnectBaseDelayMs = 10,
            MaxReconnectAttempts = 2,
        };
        var transport = new RabbitMqTransport(options, NullLogger<RabbitMqTransport>.Instance);
        var services = new ServiceCollection().BuildServiceProvider();
        var consumer = new RabbitMqConsumerService(
            transport, services.GetRequiredService<IServiceScopeFactory>(), new DefaultMessageRouter(), [], [],
            NullLogger<RabbitMqConsumerService>.Instance, options: options);

        try
        {
            await consumer.StartAsync(CancellationToken.None).ConfigureAwait(true);
            await consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true);

            consumer.ExecuteTask.IsFaulted.Should().BeFalse("giving up leaves the host running");
            transport.Status.Should().Be(TransportStatus.Faulted);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None).ConfigureAwait(true);
            await transport.DisposeAsync().ConfigureAwait(true);
            await services.DisposeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>A transport whose connect fails until attempt <c>succeedAt</c>.</summary>
    private sealed class ATransportThatConnectsAtAttempt(int succeedAt) : IMessageTransport
    {
        private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);
        public Task Connected => _connected.Task;
        public string Name => "flaky";
        public TransportStatus Status { get; private set; } = TransportStatus.Disconnected;

        public Task ConnectAsync(CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _attempts) < succeedAt)
            {
                Status = TransportStatus.Faulted;
                return Task.FromException(new InvalidOperationException("the broker is down"));
            }

            Status = TransportStatus.Connected;
            _connected.TrySetResult();
            return Task.CompletedTask;
        }

        public Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IAsyncDisposable> SubscribeAsync(string topic, string subscriptionName,
            Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler, CancellationToken ct = default)
            => throw new InvalidOperationException("nothing subscribes in this suite");

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
