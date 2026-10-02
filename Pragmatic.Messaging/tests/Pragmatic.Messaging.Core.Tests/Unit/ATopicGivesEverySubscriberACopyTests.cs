using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Channels;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Core.Tests.Unit;

/// <summary>
///     A topic gives <b>every</b> subscription a copy; a queue gives exactly one consumer the message.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The Channels transport did neither: <c>SubscribeAsync</c> did
///         <c>GetOrCreateChannel(topic)</c> — <b>one channel per topic</b> — and every subscription read
///         the <em>same</em> <c>channel.Reader</c>. <c>System.Threading.Channels</c> readers compete, so
///         a published message went to exactly one of them. N subscriptions on a topic were N competing
///         consumers, which is what a queue is.
///     </para>
///     <para>
///         ⚠️⚠️ <b>And the message was not mis-delivered, it was lost.</b>
///         <c>TransportSubscriptionBinder</c> drops what is not its own message type — "it is another
///         subscription's, already on its way there" — which is true where a topic fans out
///         and false here: the subscription had already taken the message off the shared channel, so
///         returning threw it away. No error, no dead letter, nothing logged.
///     </para>
///     <para>
///         Measured on Showcase: <c>ReservationConfirmed</c> and <c>GuestArrived</c> share the
///         topic <c>booking.events</c>, and once a saga's steps got their own subscriptions there, a
///         replayed <c>ReservationConfirmed</c> reached the Billing handler only when it won the race.
///         Probabilistic, so the suite was green once and failed on every run after.
///     </para>
/// </remarks>
public class ATopicGivesEverySubscriberACopyTests
{
    private const string Topic = "booking.events";
    private const string Queue = "orders.commands.place-order";

    private static ChannelTransport Transport(int consumerCount = 1)
        => new(new ChannelOptions { ConsumerCount = consumerCount },
            NullLogger<ChannelTransport>.Instance);

    private static ReadOnlyMemory<byte> Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> Into(
        ConcurrentQueue<string> received)
        => (payload, _, _) =>
        {
            received.Enqueue(Encoding.UTF8.GetString(payload.Span));
            return Task.CompletedTask;
        };

    /// <summary>Waits for a condition, and fails saying what never happened.</summary>
    private static async Task EventuallyAsync(Func<bool> condition, string what)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
                return;

            await Task.Delay(20).ConfigureAwait(false);
        }

        throw new Xunit.Sdk.XunitException($"waited 2s and it never happened: {what}");
    }

    /// <summary>The setpoint: two subscriptions of one topic, both delivered.</summary>
    [Fact]
    public async Task TwoSubscriptionsOfOneTopic_BothGetTheMessage()
    {
        var transport = Transport();
        await using (transport.ConfigureAwait(true))
        {
            await transport.ConnectAsync().ConfigureAwait(true);

            var first = new ConcurrentQueue<string>();
            var second = new ConcurrentQueue<string>();

            await transport.SubscribeAsync(Topic, "billing.reservation-confirmed", Into(first))
                .ConfigureAwait(true);
            await transport.SubscribeAsync(Topic, "booking.guest-arrived", Into(second))
                .ConfigureAwait(true);

            await transport.PublishAsync(Bytes("one"), Topic, MessageContext.New()).ConfigureAwait(true);

            await EventuallyAsync(
                () => first.Count == 1 && second.Count == 1,
                $"both subscriptions received it — first={first.Count}, second={second.Count}. A topic "
                + "that delivers to one of its subscribers is a queue, and the one that did not want it "
                + "drops what it took").ConfigureAwait(true);
        }
    }

    /// <summary>
    ///     ⚠️ The control: a <b>queue</b> still goes to exactly one consumer.
    /// </summary>
    /// <remarks>
    ///     This is the half the shared channel got right, and fanning out everything would break it:
    ///     <c>SendAsync</c> is addressed by message and delivered once, which is what
    ///     <c>GetSendQueue</c> exists for. "Everyone gets a copy" is the wrong answer here, so the two
    ///     paths cannot share one implementation.
    /// </remarks>
    [Fact]
    public async Task TwoConsumersOfOneQueue_GetTheMessageOnce()
    {
        var transport = Transport();
        await using (transport.ConfigureAwait(true))
        {
            await transport.ConnectAsync().ConfigureAwait(true);

            var delivered = new ConcurrentQueue<string>();

            await transport.SubscribeAsync(Queue, "worker-a", Into(delivered)).ConfigureAwait(true);
            await transport.SubscribeAsync(Queue, "worker-b", Into(delivered)).ConfigureAwait(true);

            await transport.SendAsync(Bytes("one"), Queue, MessageContext.New()).ConfigureAwait(true);

            await EventuallyAsync(() => delivered.Count == 1, "one consumer took it").ConfigureAwait(true);
            await Task.Delay(150).ConfigureAwait(true);

            delivered.Count.Should().Be(1, "point-to-point is delivered once, not to everyone");
        }
    }

    /// <summary>
    ///     ⚠️ The second control: one subscription gets the message <b>once</b>, not once per consumer
    ///     task.
    /// </summary>
    /// <remarks>
    ///     <c>ChannelOptions.ConsumerCount</c> starts N reader loops for one subscription so it can
    ///     process in parallel. Those are competing readers of that subscription's own channel and must
    ///     stay so — fanning out per reader instead of per subscription would run every handler N times,
    ///     which is this defect turned inside out.
    /// </remarks>
    [Fact]
    public async Task OneSubscriptionWithManyConsumers_GetsItOnce()
    {
        var transport = Transport(consumerCount: 4);
        await using (transport.ConfigureAwait(true))
        {
            await transport.ConnectAsync().ConfigureAwait(true);

            var delivered = new ConcurrentQueue<string>();

            await transport.SubscribeAsync(Topic, "billing.reservation-confirmed", Into(delivered))
                .ConfigureAwait(true);

            await transport.PublishAsync(Bytes("one"), Topic, MessageContext.New()).ConfigureAwait(true);

            await EventuallyAsync(() => delivered.Count == 1, "the subscription received it")
                .ConfigureAwait(true);
            await Task.Delay(150).ConfigureAwait(true);

            delivered.Count.Should().Be(1,
                "four consumer tasks share one subscription, they do not multiply it");
        }
    }

    /// <summary>Each subscription keeps receiving: the fan-out is not a one-off.</summary>
    [Fact]
    public async Task EverySubscription_KeepsReceiving()
    {
        var transport = Transport();
        await using (transport.ConfigureAwait(true))
        {
            await transport.ConnectAsync().ConfigureAwait(true);

            var first = new ConcurrentQueue<string>();
            var second = new ConcurrentQueue<string>();

            await transport.SubscribeAsync(Topic, "a", Into(first)).ConfigureAwait(true);
            await transport.SubscribeAsync(Topic, "b", Into(second)).ConfigureAwait(true);

            for (var i = 0; i < 5; i++)
                await transport.PublishAsync(Bytes($"m{i}"), Topic, MessageContext.New())
                    .ConfigureAwait(true);

            await EventuallyAsync(
                () => first.Count == 5 && second.Count == 5,
                $"both received all five — first={first.Count}, second={second.Count}")
                .ConfigureAwait(true);
        }
    }
}
