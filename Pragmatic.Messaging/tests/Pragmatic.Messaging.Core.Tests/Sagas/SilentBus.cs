namespace Pragmatic.Messaging.Core.Tests.Sagas;

/// <summary>A bus nobody listens on: the saga under test publishes nothing.</summary>
public sealed class SilentBus : IMessageBus
{
    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull => Task.CompletedTask;

    public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
        => Task.CompletedTask;

    public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default) => Task.CompletedTask;

    public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull => Task.CompletedTask;

    public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
        => Task.CompletedTask;

    public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : notnull
        where TResponse : notnull
        => throw new NotSupportedException("the saga under test asks nothing");
}
