using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.Routing;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     A claim left behind by a process that died does not swallow the message forever.
/// </summary>
/// <remarks>
///     <para>
///         The consumer claims the message id, then dispatches to the handlers. A process killed
///         between those two comes back, the transport redelivers, the claim is found, and the message
///         is <b>dropped without any handler having run</b>. The claim said "seen"; what it meant was
///         "somebody started".
///     </para>
///     <para>
///         ⚠️ <b>The producer's answer does not transfer here.</b> The outbox claims after the publish, trading a lost message for a possible duplicate publish — which is what
///         at-least-once delivery is. Do that on this side and a crash mid-handler means the handler
///         <b>runs again</b>, which is exactly what this claim exists to prevent. So the claim stays in
///         front, and gains what it was missing: a state, and a lease that lets a dead holder be
///         displaced instead of waiting forever.
///     </para>
/// </remarks>
#pragma warning disable CA2007
public class AClaimLeftByADeadProcessTests
{
    [Fact]
    public async Task AStaleInProgressClaim_IsTakenOverAndTheMessageIsHandled()
    {
        var handler = new OrderPlacedHandler();
        await using var sp = BuildServiceProvider(handler);

        var store = new InMemoryIdempotencyStore();
        var context = MessageContext.New();

        // The state a killed process leaves: claimed, never completed. The lease is already over.
        (await store.TryClaimAsync(context.MessageId, TimeSpan.Zero))
            .Should().Be(MessageClaim.Claimed, "the setup has to actually take the claim");

        var bus = Bus(sp, store);
        await bus.DispatchAsync(new OrderPlaced(Guid.NewGuid(), 10m), context);

        handler.Received.Should().ContainSingle(
            "the holder is gone and its lease has run out, so this delivery takes the claim over — "
            + "dropping it means the message is never handled by anybody");
    }

    /// <summary>
    ///     The control that forbids the easy wrong fix: a claim that is <b>completed</b> still drops
    ///     the duplicate.
    /// </summary>
    /// <remarks>
    ///     Without it, "a stale claim is taken over" is equally satisfied by ignoring claims
    ///     altogether — which is the double handling this whole mechanism exists to prevent.
    /// </remarks>
    [Fact]
    public async Task ACompletedClaim_StillDropsTheDuplicate()
    {
        var handler = new OrderPlacedHandler();
        await using var sp = BuildServiceProvider(handler);

        var store = new InMemoryIdempotencyStore();
        var context = MessageContext.New();
        var bus = Bus(sp, store);

        await bus.DispatchAsync(new OrderPlaced(Guid.NewGuid(), 10m), context);
        await bus.DispatchAsync(new OrderPlaced(Guid.NewGuid(), 10m), context);

        handler.Received.Should().ContainSingle("the first delivery completed, so the redelivery is a duplicate");
    }

    /// <summary>
    ///     And the second control: a claim still held by a live holder is not stolen.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is what separates a lease from no claim at all. Taking over a claim whose lease is
    ///     still running means two workers handle the same message at the same time, which is worse
    ///     than either failure this story is about.
    /// </remarks>
    [Fact]
    public async Task AClaimStillWithinItsLease_IsNotTakenOver()
    {
        var handler = new OrderPlacedHandler();
        await using var sp = BuildServiceProvider(handler);

        var store = new InMemoryIdempotencyStore();
        var context = MessageContext.New();

        (await store.TryClaimAsync(context.MessageId, TimeSpan.FromMinutes(30)))
            .Should().Be(MessageClaim.Claimed);

        var bus = Bus(sp, store);
        await bus.DispatchAsync(new OrderPlaced(Guid.NewGuid(), 10m), context);

        handler.Received.Should().BeEmpty(
            "somebody else is handling it right now; running it here as well is double handling");
    }

    private static TransportAwareMessageBus Bus(IServiceProvider sp, IIdempotencyStore store)
        => new(
            new NoopTransport(),
            new DefaultMessageRouter(),
            new JsonMessageSerializer(),
            new InMemoryMessageBus(sp, sp.GetRequiredService<ILogger<InMemoryMessageBus>>(),
                sp.GetServices<ITypedMessageDispatchTable>()),
            sp.GetRequiredService<ILogger<TransportAwareMessageBus>>(),
            store);

    private static ServiceProvider BuildServiceProvider(OrderPlacedHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging(_ => { });
        services.AddSingleton<IMessageHandler<OrderPlaced>>(handler);
        return services.BuildServiceProvider();
    }

    public record OrderPlaced(Guid OrderId, decimal Total);

    public sealed class OrderPlacedHandler : IMessageHandler<OrderPlaced>
    {
        public List<OrderPlaced> Received { get; } = [];

        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>A transport that carries nothing: this suite is about the consume path.</summary>
    private sealed class NoopTransport : IMessageTransport
    {
        public string Name => "noop";
        public TransportStatus Status => TransportStatus.Connected;

        public Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IAsyncDisposable> SubscribeAsync(string topic, string subscriptionName,
            Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler, CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable>(new NoopSubscription());

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class NoopSubscription : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
