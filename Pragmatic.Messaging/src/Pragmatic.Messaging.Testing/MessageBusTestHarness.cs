using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Messaging.Testing;

/// <summary>
///     In-memory <see cref="IMessageBus"/> for tests. Two modes:
///     <list type="bullet">
///         <item><b>Record-only</b> (parameterless, <c>AddMessagingTestHarness()</c>): records
///         published/sent messages, never dispatches.</item>
///         <item><b>Dispatching</b> (with an <see cref="IServiceProvider"/>,
///         <c>AddDispatchingTestHarness()</c>): additionally dispatches to the registered
///         <see cref="IMessageHandler{T}"/> implementations, recording
///         <see cref="Consumed"/> per handler and <see cref="Faulted"/> for handler failures.
///         Unlike the production bus, handler failures are recorded, not rethrown — the test
///         asserts on <see cref="Faulted"/> instead of catching (opt into <see cref="RethrowHandlerFailures"/>
///         to also rethrow the terminal failure). An untyped <see cref="DispatchAsync"/> /
///         <c>PublishAsync(object, Type, …)</c> that no dispatch table covers throws (a false-negative
///         guard); locally-dispatched objects are recorded in <see cref="Dispatched"/>, distinct from
///         <see cref="Published"/>.</item>
///     </list>
/// </summary>
public sealed class MessageBusTestHarness : IMessageBus
{
    private readonly ConcurrentBag<PublishedMessage> _published = [];
    private readonly ConcurrentBag<PublishedMessage> _sent = [];
    private readonly ConcurrentBag<PublishedMessage> _dispatched = [];
    private readonly ConcurrentBag<ConsumedMessage> _consumed = [];
    private readonly ConcurrentBag<FaultedMessage> _faulted = [];
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    ///     Opt-in strict mode (default <c>false</c> — behaviour unchanged). When <c>true</c>, a handler
    ///     failure that surfaces after the SG pipeline (registered as <see cref="IMessageHandler{T}"/>)
    ///     has exhausted its retries is recorded in <see cref="Faulted"/> AND rethrown, so a test can
    ///     observe the terminal failure (the nack/DLQ the production bus would trigger). Off by default
    ///     the harness only records failures — assert on <see cref="Faulted"/>.
    /// </summary>
    public bool RethrowHandlerFailures { get; set; }

    /// <summary>Record-only harness: publish/send are recorded, nothing is dispatched.</summary>
    public MessageBusTestHarness()
    {
    }

    /// <summary>
    ///     Dispatching harness: publish/send are recorded AND dispatched to the registered
    ///     handlers (resolved from a fresh scope per message).
    /// </summary>
    public MessageBusTestHarness(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    /// <summary>All messages published (fan-out) so far.</summary>
    public IReadOnlyList<PublishedMessage> Published => [.. _published];

    /// <summary>All messages sent (point-to-point) so far.</summary>
    public IReadOnlyList<PublishedMessage> Sent => [.. _sent];

    /// <summary>
    ///     All messages routed through <see cref="DispatchAsync"/> (consumer-side local dispatch of an
    ///     untyped object) so far. Distinct from <see cref="Published"/> so a test can tell a producer
    ///     <c>Publish</c> apart from a local <c>Dispatch</c>.
    /// </summary>
    public IReadOnlyList<PublishedMessage> Dispatched => [.. _dispatched];

    /// <summary>All (message, handler) pairs that completed successfully (dispatching mode).</summary>
    public IReadOnlyList<ConsumedMessage> Consumed => [.. _consumed];

    /// <summary>All (message, handler, exception) failures (dispatching mode).</summary>
    public IReadOnlyList<FaultedMessage> Faulted => [.. _faulted];

    /// <summary>Returns all published messages of type <typeparamref name="T"/>.</summary>
    public IReadOnlyList<T> PublishedOf<T>() where T : notnull
        => [.. _published.Where(m => m.MessageType == typeof(T)).Select(m => (T)m.Message)];

    /// <summary>Returns all sent messages of type <typeparamref name="T"/>.</summary>
    public IReadOnlyList<T> SentOf<T>() where T : notnull
        => [.. _sent.Where(m => m.MessageType == typeof(T)).Select(m => (T)m.Message)];

    /// <summary>Returns all locally-dispatched messages of type <typeparamref name="T"/>.</summary>
    public IReadOnlyList<T> DispatchedOf<T>() where T : notnull
        => [.. _dispatched.Where(m => m.MessageType == typeof(T)).Select(m => (T)m.Message)];

    /// <summary>Returns all consumed messages of type <typeparamref name="T"/>.</summary>
    public IReadOnlyList<T> ConsumedOf<T>() where T : notnull
        => [.. _consumed.Where(m => m.MessageType == typeof(T)).Select(m => (T)m.Message)];

    /// <summary>Returns all faulted entries for messages of type <typeparamref name="T"/>.</summary>
    public IReadOnlyList<FaultedMessage> FaultedOf<T>() where T : notnull
        => [.. _faulted.Where(m => m.MessageType == typeof(T))];

    /// <summary>Returns true if any message of type <typeparamref name="T"/> was published.</summary>
    public bool HasPublished<T>() where T : notnull
        => _published.Any(m => m.MessageType == typeof(T));

    /// <summary>Returns true if any message of type <typeparamref name="T"/> matching the predicate was published.</summary>
    public bool HasPublished<T>(Func<T, bool> predicate) where T : notnull
        => _published.Where(m => m.MessageType == typeof(T)).Select(m => (T)m.Message).Any(predicate);

    /// <summary>Returns true if any message of type <typeparamref name="T"/> was sent (point-to-point).</summary>
    public bool HasSent<T>() where T : notnull
        => _sent.Any(m => m.MessageType == typeof(T));

    /// <summary>Returns true if any message of type <typeparamref name="T"/> was locally dispatched.</summary>
    public bool HasDispatched<T>() where T : notnull
        => _dispatched.Any(m => m.MessageType == typeof(T));

    /// <summary>Returns true if a handler consumed a message of type <typeparamref name="T"/>.</summary>
    public bool HasConsumed<T>() where T : notnull
        => _consumed.Any(m => m.MessageType == typeof(T));

    /// <summary>Returns true if a handler consumed a matching message of type <typeparamref name="T"/>.</summary>
    public bool HasConsumed<T>(Func<T, bool> predicate) where T : notnull
        => _consumed.Where(m => m.MessageType == typeof(T)).Select(m => (T)m.Message).Any(predicate);

    /// <summary>Returns true if a handler faulted on a message of type <typeparamref name="T"/>.</summary>
    public bool HasFaulted<T>() where T : notnull
        => _faulted.Any(m => m.MessageType == typeof(T));

    /// <summary>Clears all recorded messages.</summary>
    public void Reset()
    {
        _published.Clear();
        _sent.Clear();
        _dispatched.Clear();
        _consumed.Clear();
        _faulted.Clear();
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull
        => PublishAsync(message, MessageContext.New(), ct);

    /// <inheritdoc />
    public async Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
    {
        _published.Add(new PublishedMessage(typeof(T), message, context, DateTimeOffset.UtcNow));
        await DispatchToHandlersAsync(message, context, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default)
    {
        _dispatched.Add(new PublishedMessage(message.GetType(), message, context, DateTimeOffset.UtcNow));

        // When a generated dispatch table covers the type, the typed re-entry records the
        // publish (and dispatches); recording here too would duplicate the entry.
        if (await DispatchUntypedAsync(message, context, ct).ConfigureAwait(false))
            return;

        // Dispatching mode with no table for the type: the message never reaches handlers. Fail loudly
        // rather than record a false-negative success (record-only mode legitimately just records).
        if (_serviceProvider is not null)
            ThrowUntypedNotDispatched(message.GetType());

        _published.Add(new PublishedMessage(message.GetType(), message, context, DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public async Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
    {
        if (await DispatchUntypedAsync(message, context, ct).ConfigureAwait(false))
            return;

        if (_serviceProvider is not null)
            ThrowUntypedNotDispatched(messageType);

        _published.Add(new PublishedMessage(messageType, message, context, DateTimeOffset.UtcNow));
    }

    private static void ThrowUntypedNotDispatched(Type messageType)
        => throw new InvalidOperationException(
            $"Dispatching MessageBusTestHarness received an untyped message of type '{messageType}' that no " +
            "registered ITypedMessageDispatchTable covers, so it was NOT dispatched to handlers. Register the " +
            "SG-generated dispatch table for its assembly in the test's service provider (or use the typed " +
            "PublishAsync<T> overload). Silently recording it would make HasConsumed<T>() a false negative.");

    /// <inheritdoc />
    public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull
        => SendAsync(message, MessageContext.New(), ct);

    /// <inheritdoc />
    public async Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
    {
        _sent.Add(new PublishedMessage(typeof(T), message, context, DateTimeOffset.UtcNow));
        await DispatchToHandlersAsync(message, context, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : notnull
        where TResponse : notnull
    {
        // Record the request before resolving so test assertions can verify the call was made.
        _sent.Add(new PublishedMessage(typeof(TRequest), request, null, DateTimeOffset.UtcNow));

        if (_serviceProvider is null)
        {
            throw new InvalidOperationException(
                $"MessageBusTestHarness (record-only) does not support RequestAsync. " +
                $"The request was recorded but no handler is wired. Use AddDispatchingTestHarness() " +
                $"or a mock IRequestHandler<{typeof(TRequest).Name}, {typeof(TResponse).Name}>.");
        }

        using var scope = _serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException(
                $"No IRequestHandler<{typeof(TRequest).Name}, {typeof(TResponse).Name}> registered.");

        var response = await handler.HandleAsync(request, MessageContext.New(), ct).ConfigureAwait(false);
        _consumed.Add(new ConsumedMessage(typeof(TRequest), request, handler.GetType(), null, DateTimeOffset.UtcNow));
        return response;
    }

    /// <summary>Dispatches to registered handlers (dispatching mode only), recording consumed/faulted per handler.</summary>
    private async Task DispatchToHandlersAsync<T>(T message, MessageContext context, CancellationToken ct) where T : notnull
    {
        if (_serviceProvider is null)
            return;

        using var scope = _serviceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IMessageHandler<T>>().OrderBy(h => h.Order);

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(message, context, ct).ConfigureAwait(false);
                _consumed.Add(new ConsumedMessage(typeof(T), message, handler.GetType(), context, DateTimeOffset.UtcNow));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Test-harness contract: failures are recorded, not rethrown — assert on Faulted.
                _faulted.Add(new FaultedMessage(typeof(T), message, handler.GetType(), ex, DateTimeOffset.UtcNow));

                // Opt-in strict mode: also surface the terminal failure so a test can observe the
                // nack/DLQ the production bus would trigger after the pipeline exhausts its retries.
                if (RethrowHandlerFailures)
                    throw;
            }
        }
    }

    /// <summary>
    ///     Routes an untyped message back through the typed path via the SG dispatch tables
    ///     (the generated table calls this harness's typed PublishAsync, which records and
    ///     dispatches). Returns true when a table covered the type.
    /// </summary>
    private async Task<bool> DispatchUntypedAsync(object message, MessageContext context, CancellationToken ct)
    {
        if (_serviceProvider is null)
            return false;

        foreach (var table in _serviceProvider.GetServices<ITypedMessageDispatchTable>())
        {
            if (table.TryDispatch(this, message, context, ct) is { } dispatched)
            {
                await dispatched.ConfigureAwait(false);
                return true;
            }
        }

        return false;
    }
}
