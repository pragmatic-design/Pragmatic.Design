using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Routing;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Core.Tests.Unit;

/// <summary>
///     A consumer service closes every subscription it bound, including one that its bind returned after the host had stopped it (#551).
/// </summary>
/// <remarks>
///     <para>
///         The subscriptions are bound in <c>ExecuteAsync</c>, which runs in the background, and <c>BackgroundService.StopAsync</c> returns
///         when that finishes <b>or</b> when the stop's token fires. The services closed them in <c>StopAsync</c>: a bind still running when
///         the stop gave up added its subscriptions after the loop that closed them, and they stayed open on a stopped host, or it added them
///         during the loop, and the stop threw "Collection was modified" (Warehouse.IntegrationTests on CI, PR #550).
///     </para>
///     <para>
///         Choreographed rather than hoped for: the transport holds the second subscribe until the test releases it, and the stop is given a
///         token that has already fired, so it returns with the bind still open on every run.
///     </para>
/// </remarks>
public sealed class ASubscriptionBoundAfterTheStopIsStillClosedTests
{
    [Fact]
    public async Task NamedBus_ASubscriptionTheBindReturnsAfterTheStop_IsClosed_WithTheOneBoundBefore()
    {
        var transport = new ATransportThatHoldsTheSecondSubscribe();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var consumer = new NamedBusConsumerService(
            "orders", transport, services.GetRequiredService<IServiceScopeFactory>(), new DefaultMessageRouter(),
            [new MessageSubscription(typeof(FirstMessage), "listener", "orders"), new MessageSubscription(typeof(SecondMessage), "listener", "orders")],
            NullLogger<NamedBusConsumerService>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await transport.SecondSubscribeEntered.WaitAsync(TimeSpan.FromSeconds(10));

        using var gaveUp = new CancellationTokenSource();
        await gaveUp.CancelAsync();
        await consumer.StopAsync(gaveUp.Token);

        transport.ReleaseSecondSubscribe();
        await consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        transport.Handles.Should().HaveCount(2, "both subscriptions were bound");
        transport.Handles.Count(h => h.Disposed).Should().Be(2,
            "a subscription the service bound is closed by it, whenever its bind returned");
        consumer.Dispose();
    }

    /// <summary>A bind cancelled halfway closes what it had bound: the caller never receives those handles, so nobody else can.</summary>
    [Fact]
    public async Task Bind_CancelledHalfway_ClosesTheSubscriptionsItHadMade()
    {
        var transport = new ATransportThatHoldsTheSecondSubscribe { CancelTheSecond = true };
        await using var services = new ServiceCollection().BuildServiceProvider();

        var bind = () => TransportSubscriptionBinder.BindAsync(
            transport, new DefaultMessageRouter(), services.GetRequiredService<IServiceScopeFactory>(),
            [new MessageSubscription(typeof(FirstMessage), "listener"), new MessageSubscription(typeof(SecondMessage), "listener")]);

        await bind.Should().ThrowAsync<OperationCanceledException>();
        transport.Handles.Should().ContainSingle("the first subscription was made before the second was cancelled");
        transport.Handles.Single().Disposed.Should().BeTrue("a handle the bind cannot return is closed by the bind");
    }

    private sealed record FirstMessage;

    private sealed record SecondMessage;

    /// <summary>Subscribes the first topic at once and holds the second until released, or cancels it.</summary>
    private sealed class ATransportThatHoldsTheSecondSubscribe : IMessageTransport
    {
        private readonly TaskCompletionSource _secondEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _handles = new();
        private int _subscribes;

        public bool CancelTheSecond { get; init; }
        public List<Handle> Handles { get; } = [];
        public Task SecondSubscribeEntered => _secondEntered.Task;
        public string Name => "held";
        public TransportStatus Status => TransportStatus.Connected;

        public void ReleaseSecondSubscribe() => _release.TrySetResult();

        public async Task<IAsyncDisposable> SubscribeAsync(string topic, string subscriptionName,
            Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _subscribes) > 1)
            {
                if (CancelTheSecond)
                    throw new OperationCanceledException("the host stopped during the bind");

                _secondEntered.TrySetResult();
                await _release.Task.ConfigureAwait(false); // a broker's subscribe that does not observe the token
            }

            var handle = new Handle();
            lock (_handles)
                Handles.Add(handle);
            return handle;
        }

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Handle : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
