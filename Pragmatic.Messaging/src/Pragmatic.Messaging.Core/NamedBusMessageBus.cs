using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging;

/// <summary>
///     Composite message bus that routes messages to named buses based on handler configuration.
///     Handlers decorated with <c>[OnBus("analytics")]</c> publish/subscribe via the named bus.
///     Handlers without <c>[OnBus]</c> use the default bus.
/// </summary>
/// <remarks>
///     Auto-registered as the active <see cref="IMessageBus"/> by <c>AddPragmaticMessaging</c>
///     when named buses with isolated transports exist (see
///     <see cref="Configuration.MessagingBuilder.AddBus"/>). Routes via the
///     <see cref="MessageContext.Headers"/> <c>bus.name</c> header or, as a fallback, the
///     <see cref="MessageContext.BusName"/> property; the per-name keyed
///     <see cref="IMessageBus"/> remains directly resolvable.
/// </remarks>
public sealed class NamedBusMessageBus(
    IMessageBus defaultBus,
    IReadOnlyDictionary<string, IMessageBus> namedBuses,
    IMessageRouter? router = null)
    : IMessageBus
{
    private readonly IMessageRouter? _router = router;

    /// <summary>
    ///     Resolves the target bus for a message: the <c>bus.name</c> header wins (explicit producer
    ///     knob), falling back to the <see cref="MessageContext.BusName"/> property. Both name an
    ///     entry in the named-bus map; anything unmatched (or unset) routes to the default bus.
    /// </summary>
    private IMessageBus ResolveBus(MessageContext context)
    {
        var name = context.Headers?.TryGetValue("bus.name", out var header) == true ? header : context.BusName;
        return name is not null && namedBuses.TryGetValue(name, out var bus) ? bus : defaultBus;
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull
        => defaultBus.PublishAsync(message, ct);

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
        => ResolveBus(context).PublishAsync(message, context, ct);

    /// <inheritdoc />
    public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull
        => defaultBus.SendAsync(message, ct);

    /// <inheritdoc />
    public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
        => ResolveBus(context).SendAsync(message, context, ct);

    /// <inheritdoc />
    public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : notnull
        where TResponse : notnull
        => defaultBus.RequestAsync<TRequest, TResponse>(request, ct);

    /// <inheritdoc />
    public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default)
        => ResolveBus(context).DispatchAsync(message, context, ct);

    /// <inheritdoc />
    public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
        => ResolveBus(context).PublishAsync(message, messageType, context, ct);

    /// <summary>
    ///     Resolves the bus for a specific named bus. Returns default if not found.
    /// </summary>
    public IMessageBus GetBus(string? busName)
    {
        if (busName is not null && namedBuses.TryGetValue(busName, out var bus))
            return bus;
        return defaultBus;
    }
}
