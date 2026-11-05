using System.Collections.Concurrent;
using Pragmatic.Messaging;

namespace PragmaticSagas.Tests;

/// <summary>The bus the saga publishes to when a message does not go through the outbox: what it published, in order.</summary>
public sealed class SagaBus : IMessageBus
{
    public ConcurrentQueue<object> Published { get; } = new();

    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull
    {
        Published.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => PublishAsync(message, ct);

    public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default) => PublishAsync(message, ct);

    public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default) => throw new NotSupportedException();

    public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();

    public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();

    public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : notnull where TResponse : notnull => throw new NotSupportedException();
}
