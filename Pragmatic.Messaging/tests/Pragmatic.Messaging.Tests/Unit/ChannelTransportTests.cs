using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Channels;
using ChannelOptions = Pragmatic.Messaging.Channels.ChannelOptions;

namespace Pragmatic.Messaging.Tests.Unit;

public class ChannelTransportTests : IAsyncDisposable
{
    private readonly ChannelTransport _transport;

    public ChannelTransportTests()
    {
        var options = new ChannelOptions { Capacity = 100, ConsumerCount = 1 };
        _transport = new ChannelTransport(options, NullLogger<ChannelTransport>.Instance);
    }

    public async ValueTask DisposeAsync() => await _transport.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public void Name_ShouldBeChannels()
    {
        _transport.Name.Should().Be("Channels");
    }

    [Fact]
    public async Task ConnectAsync_ShouldSetStatusToConnected()
    {
        _transport.Status.Should().Be(TransportStatus.Disconnected);
        await _transport.ConnectAsync();
        _transport.Status.Should().Be(TransportStatus.Connected);
    }

    [Fact]
    public async Task DisconnectAsync_ShouldSetStatusToDisconnected()
    {
        await _transport.ConnectAsync();
        await _transport.DisconnectAsync();
        _transport.Status.Should().Be(TransportStatus.Disconnected);
    }

    [Fact]
    public async Task PublishAndSubscribe_ShouldDeliverMessage()
    {
        var received = new TaskCompletionSource<(ReadOnlyMemory<byte> Payload, MessageContext Context)>();

        await _transport.SubscribeAsync("test-topic", "test-sub", (payload, ctx, ct) =>
        {
            received.SetResult((payload, ctx));
            return Task.CompletedTask;
        });

        var context = MessageContext.New(correlationId: "corr-1");
        var payload = System.Text.Encoding.UTF8.GetBytes("hello");

        await _transport.PublishAsync(payload, "test-topic", context);

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        System.Text.Encoding.UTF8.GetString(result.Payload.Span).Should().Be("hello");
        result.Context.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task HandlerFailure_WithDeadLetterStore_StoresMessage()
    {
        var store = new InMemoryDeadLetterStore();
        var transport = new ChannelTransport(
            new ChannelOptions { Capacity = 10, ConsumerCount = 1 },
            NullLogger<ChannelTransport>.Instance,
            store);
        try
        {
            await transport.ConnectAsync();

            await transport.SubscribeAsync("dl-topic", "dl-sub",
                (_, _, _) => throw new InvalidOperationException("boom"));

            var context = MessageContext.New(correlationId: "corr-dl");
            await transport.PublishAsync(System.Text.Encoding.UTF8.GetBytes("""{"x":1}"""), "dl-topic", context);

            // The consumer loop is async: poll until the dead letter lands.
            IReadOnlyList<DeadLetterMessage> dead = [];
            for (var i = 0; i < 50 && dead.Count == 0; i++)
            {
                await Task.Delay(100);
                dead = await store.GetAllAsync();
            }

            dead.Should().ContainSingle();
            dead[0].MessageType.Should().Be("dl-topic");
            dead[0].Error.Should().Be("boom");
            dead[0].Payload.Should().Contain("\"x\":1");
            dead[0].Context.CorrelationId.Should().Be("corr-dl");
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task PublishToUnsubscribedTopic_ShouldNotThrow()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("orphan");
        var context = MessageContext.New();

        // Publishing to a topic with no subscribers should not throw
        await _transport.Invoking(t => t.PublishAsync(payload, "no-sub", context))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task MultipleMessages_ShouldDeliverAll()
    {
        var count = 0;
        var allReceived = new TaskCompletionSource();

        await _transport.SubscribeAsync("multi-topic", "multi-sub", (_, _, _) =>
        {
            if (Interlocked.Increment(ref count) >= 5)
                allReceived.SetResult();
            return Task.CompletedTask;
        });

        for (var i = 0; i < 5; i++)
            await _transport.PublishAsync(new byte[] { (byte)i }, "multi-topic", MessageContext.New());

        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        count.Should().Be(5);
    }

    [Fact]
    public async Task Subscribe_ShouldReturnDisposableSubscription()
    {
        var sub = await _transport.SubscribeAsync("dispose-topic", "dispose-sub",
            (_, _, _) => Task.CompletedTask);

        sub.Should().NotBeNull();
        await sub.DisposeAsync();
    }

    [Fact]
    public async Task SendAsync_ShouldWorkLikePublish()
    {
        var received = new TaskCompletionSource<bool>();

        await _transport.SubscribeAsync("queue-1", "consumer-1", (_, _, _) =>
        {
            received.SetResult(true);
            return Task.CompletedTask;
        });

        await _transport.SendAsync(new byte[] { 1 }, "queue-1", MessageContext.New());

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeTrue();
    }

    /// <summary>
    ///     The health check sees what a subscription has not consumed yet.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Arranged with a subscriber that is busy, not with <b>no subscriber at all</b>. A topic fans
    ///     out to one channel <em>per subscription</em>, so there is nowhere to hold a message nobody
    ///     subscribes to, and a broker discards it too.
    /// </remarks>
    [Fact]
    public async Task GetPendingCount_ShouldReflectUnconsumedMessages()
    {
        var blocked = new TaskCompletionSource();
        var taken = new TaskCompletionSource();

        await _transport.SubscribeAsync("pending-topic", "slow-consumer", async (_, _, _) =>
        {
            taken.TrySetResult();
            await blocked.Task.ConfigureAwait(false);
        });

        // Three published, one taken by the consumer that will not return: two are left waiting.
        for (var i = 0; i < 3; i++)
            await _transport.PublishAsync(new byte[] { (byte)i }, "pending-topic", MessageContext.New());

        await taken.Task.WaitAsync(TimeSpan.FromSeconds(5));

        for (var attempt = 0; attempt < 50 && _transport.GetPendingCount("pending-topic") != 2; attempt++)
            await Task.Delay(20);

        _transport.GetPendingCount("pending-topic").Should().Be(2);
        _transport.GetPendingCount("nonexistent").Should().Be(0);

        blocked.SetResult();
    }

    /// <summary>
    ///     ⚠️ A publish nobody subscribes to is discarded, exactly as a topic exchange with no bound
    ///     queue discards.
    /// </summary>
    /// <remarks>
    ///     The rule that makes the fan-out possible, pinned so it is a decision rather than a side
    ///     effect: holding the message for a subscriber that may arrive later is what the shared
    ///     per-topic channel did, and that sharing is the defect.
    /// </remarks>
    [Fact]
    public async Task PublishWithNoSubscriber_IsDiscarded()
    {
        await _transport.PublishAsync(new byte[] { 1 }, "nobody-listens", MessageContext.New());

        _transport.GetPendingCount("nobody-listens").Should().Be(0);

        // The control: a subscriber that arrives afterwards is not handed the past.
        var received = 0;
        await _transport.SubscribeAsync("nobody-listens", "late", (_, _, _) =>
        {
            Interlocked.Increment(ref received);
            return Task.CompletedTask;
        });

        await Task.Delay(100);

        received.Should().Be(0, "the message was discarded when it was published, not queued for later");
    }

    [Fact]
    public async Task HandlerError_ShouldNotStopConsumer()
    {
        var callCount = 0;
        var secondReceived = new TaskCompletionSource();

        await _transport.SubscribeAsync("error-topic", "error-sub", (_, _, _) =>
        {
            var c = Interlocked.Increment(ref callCount);
            if (c == 1) throw new InvalidOperationException("First message fails");
            secondReceived.SetResult();
            return Task.CompletedTask;
        });

        await _transport.PublishAsync(new byte[] { 1 }, "error-topic", MessageContext.New());
        await _transport.PublishAsync(new byte[] { 2 }, "error-topic", MessageContext.New());

        await secondReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callCount.Should().BeGreaterThanOrEqualTo(2);
    }
}
