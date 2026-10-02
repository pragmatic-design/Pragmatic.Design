using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Entities;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     A publish that failed leaves no claim behind, so the retry re-publishes instead of finding a
///     claim, marking the row processed, and losing the message.
/// </summary>
/// <remarks>
///     ⚠️ The claim is written <b>after</b> a successful publish, so there is nothing to release: the
///     assertion below passes because nothing was ever claimed. What it guards is the outcome — no
///     claim after a failed publish — and that outcome is what must survive whichever way the service
///     arranges it.
/// </remarks>
#pragma warning disable CA2007
public class OutboxIdempotencyFailureTests
{
    [Fact]
    public async Task PublishFailure_ReleasesIdempotencyClaim_AndDoesNotMarkProcessed()
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = "MyApp.SomeEvent",
            Payload = "{}",
            RetryCount = 0,
        };

        var source = new OneMessageSource(message);
        var idempotency = new InMemoryIdempotencyStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOutboxSource>(source);
        services.AddSingleton<IMessageBus>(new ThrowingBus());
        services.AddSingleton<IMessageTypeRegistry>(new EchoRegistry());
        services.AddSingleton<IIdempotencyStore>(idempotency);
        services.AddSingleton(Options.Create(new MessagingOptions { PollingIntervalSeconds = 1, MaxRetries = 5 }));

        await using var sp = services.BuildServiceProvider();

        var service = new OutboxDeliveryService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<MessagingOptions>>(),
            sp.GetRequiredService<ILogger<OutboxDeliveryService>>());

        await service.StartAsync(CancellationToken.None);
        // The first delivery batch runs immediately on start; wait for it to complete.
        await WaitUntilAsync(() => source.MarkFailedCalls + source.MarkProcessedCalls > 0, TimeSpan.FromSeconds(3));
        await service.StopAsync(CancellationToken.None);

        source.MarkFailedCalls.Should().Be(1, "the publish failed, so the message is failed/retried — not lost");
        source.MarkProcessedCalls.Should().Be(0, "a failed publish must never mark the row processed");
        // Asked of the store and not of a key: the pump claims the publish under a key space of its own,
        // and an assertion naming the bare message id would pass because nothing ever
        // claimed it — green for the wrong reason, about the one thing this test exists to hold.
        idempotency.Count.Should().Be(0, "no claim stands for a publish that did not happen, so a retry re-publishes");
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

    private sealed class OneMessageSource(OutboxMessage message) : IOutboxSource
    {
        public int MarkProcessedCalls { get; private set; }
        public int MarkFailedCalls { get; private set; }
        private bool _consumed;

        public string BoundaryName => "Test";

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OutboxMessage>>(_consumed ? [] : [message]);

        public Task MarkProcessedAsync(Guid id, CancellationToken ct = default)
        {
            MarkProcessedCalls++;
            _consumed = true;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default)
        {
            MarkFailedCalls++;
            _consumed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class EchoRegistry : IMessageTypeRegistry
    {
        public object? Deserialize(string fullyQualifiedTypeName, string json) => new object();
    }

    /// <summary>Bus whose transport publish always fails (broker down).</summary>
    private sealed class ThrowingBus : IMessageBus
    {
        public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("broker down");

        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : notnull where TResponse : notnull => throw new NotSupportedException();
    }
}
