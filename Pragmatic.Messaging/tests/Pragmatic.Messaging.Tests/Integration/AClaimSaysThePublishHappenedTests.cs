using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Entities;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     The outbox's publish claim means "this was published", so it is written after the
///     publish and not before it.
/// </summary>
/// <remarks>
///     <para>
///         Written first, a process that died between the claim and <c>PublishAsync</c> would come
///         back, find the claim, and mark the outbox row **processed without publishing it** — the
///         message gone, and the row saying it was delivered. A claim taken before the work means
///         "somebody is trying" and would be read as "somebody succeeded"; those are two states and the
///         store has one.
///     </para>
///     <para>
///         ⚠️ This order does not remove duplicates, and is not meant to: a crash between the
///         publish and the claim re-publishes on restart. That is what at-least-once delivery is, it
///         is what an outbox exists to provide, and deduplicating the **effects** is the consumer's
///         job. Losing a message is not a milder version of delivering it twice.
///     </para>
/// </remarks>
#pragma warning disable CA2007
public class AClaimSaysThePublishHappenedTests
{
    /// <summary>
    ///     The ordering, asserted from inside the publish — which is the only place it is visible
    ///     without killing a process.
    /// </summary>
    [Fact]
    public async Task TheClaimIsNotWritten_UntilThePublishHasHappened()
    {
        var idempotency = new InMemoryIdempotencyStore();
        var bus = new RecordingBus(() => idempotency.Count);
        var source = new OneMessage();

        await RunOnceAsync(source, bus, idempotency);

        bus.Published.Should().Be(1, "the message is published, or this measures nothing");
        bus.ClaimsAtPublishTime.Should().Be(0,
            "a claim standing while the publish is still in flight is a promise the process cannot "
            + "keep: dying here loses the message and marks the row delivered");
    }

    /// <summary>
    ///     A shutdown during the publish leaves nothing behind.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The release path was guarded by <c>when (ex is not OperationCanceledException)</c>, so a
    ///     graceful shutdown — which is exactly how a cancellation arrives — skipped it and left the
    ///     claim set. The sibling test for an ordinary exception passed throughout; this one is the
    ///     case that was excluded from it by that filter.
    /// </remarks>
    [Fact]
    public async Task AShutdownDuringThePublish_LeavesNoClaim()
    {
        var idempotency = new InMemoryIdempotencyStore();
        var source = new OneMessage();

        await RunOnceAsync(source, new CancellingBus(), idempotency);

        idempotency.Count.Should().Be(0,
            "nothing was published, so nothing may claim to have been — the next sweep must re-publish");
        source.MarkProcessedCalls.Should().Be(0, "a message that was not published is not delivered");
    }

    /// <summary>
    ///     The control: the claim still does its job.
    /// </summary>
    /// <remarks>
    ///     Without it, "the claim is not written before the publish" is equally satisfied by removing
    ///     the claim altogether — which would bring back the duplicate it exists to stop, in the window
    ///     between a successful publish and the row being marked processed.
    /// </remarks>
    [Fact]
    public async Task AMessageAlreadyClaimed_IsNotPublishedAgain()
    {
        var source = new OneMessage();
        var idempotency = new InMemoryIdempotencyStore();

        // The state a crash between PublishAsync and MarkProcessedAsync leaves behind.
        await idempotency.TryMarkAsProcessedAsync("outbox-publish:" + source.Message.Id.ToString("N"));

        var bus = new RecordingBus(() => 0);
        await RunOnceAsync(source, bus, idempotency);

        bus.Published.Should().Be(0, "it was already published — this is the duplicate the claim stops");
        source.MarkProcessedCalls.Should().Be(1, "and the row is closed rather than retried forever");
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static async Task RunOnceAsync(
        OneMessage source, IMessageBus bus, InMemoryIdempotencyStore idempotency)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOutboxSource>(source);
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton<IMessageTypeRegistry>(new EchoRegistry());
        services.AddSingleton<IIdempotencyStore>(idempotency);
        services.AddSingleton(Options.Create(new MessagingOptions { PollingIntervalSeconds = 1, MaxRetries = 5 }));

        await using var sp = services.BuildServiceProvider();

        var service = new OutboxDeliveryService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<MessagingOptions>>(),
            sp.GetRequiredService<ILogger<OutboxDeliveryService>>());

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => source.Settled, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(25);
        }
    }

    private sealed class OneMessage : IOutboxSource
    {
        public OutboxMessage Message { get; } = new()
        {
            Id = Guid.NewGuid(),
            MessageType = "MyApp.SomeEvent",
            Payload = "{}",
            RetryCount = 0,
        };

        public int MarkProcessedCalls { get; private set; }
        public int MarkFailedCalls { get; private set; }
        public bool Settled { get; private set; }

        public string BoundaryName => "Test";

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OutboxMessage>>(Settled ? [] : [Message]);

        public Task MarkProcessedAsync(Guid id, CancellationToken ct = default)
        {
            MarkProcessedCalls++;
            Settled = true;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default)
        {
            MarkFailedCalls++;
            Settled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class EchoRegistry : IMessageTypeRegistry
    {
        public object? Deserialize(string fullyQualifiedTypeName, string json) => new object();
    }

    /// <summary>Publishes, and records how many claims existed while it was doing so.</summary>
    private sealed class RecordingBus(Func<int> claimCount) : IMessageBus
    {
        public int Published { get; private set; }
        public int ClaimsAtPublishTime { get; private set; }

        public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
        {
            ClaimsAtPublishTime = claimCount();
            Published++;
            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : notnull where TResponse : notnull => throw new NotSupportedException();
    }

    /// <summary>A publish interrupted by a shutdown.</summary>
    private sealed class CancellingBus : IMessageBus
    {
        public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
            => throw new OperationCanceledException("the host is stopping");

        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : notnull where TResponse : notnull => throw new NotSupportedException();
    }
}
