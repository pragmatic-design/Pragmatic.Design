using System.Collections.Concurrent;
using Pragmatic.Messaging;

namespace PragmaticBoundaries.Tests;

/// <summary>The transport, as the delivery pump sees it: every message it publishes, with the context it rebuilt from the outbox row.</summary>
public sealed class RecordingBus : IMessageBus
{
    public ConcurrentQueue<(object Message, MessageContext Context)> Published { get; } = new();

    public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
    {
        Published.Enqueue((message, context));
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
