using Pragmatic.Events;

namespace Pragmatic.Messaging;

/// <summary>
///     Bridges <see cref="IMessageHandler{T}"/> to <see cref="IDomainEventHandler{T}"/>
///     so that message handlers receive events dispatched by <c>InMemoryEventDispatcher</c>.
///     The SG registers this adapter for each [MessageHandler] that handles a domain event type.
/// </summary>
/// <typeparam name="TEvent">The domain event type.</typeparam>
public sealed class MessageHandlerEventAdapter<TEvent> : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
    // Pre-sorted once at construction so HandleAsync pays no LINQ allocation per call.
    private readonly IMessageHandler<TEvent>[] _handlers;

    public MessageHandlerEventAdapter(IEnumerable<IMessageHandler<TEvent>> handlers)
        => _handlers = [.. handlers.OrderBy(h => h.Order)];

    public int Order => 0;

    public async Task HandleAsync(TEvent @event, CancellationToken ct = default)
    {
        var context = MessageContext.New();

        foreach (var handler in _handlers)
        {
            await handler.HandleAsync(@event, context, ct).ConfigureAwait(false);
        }
    }
}
