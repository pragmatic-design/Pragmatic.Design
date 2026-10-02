using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Diagnostics;
using Pragmatic.MultiTenancy;
using Pragmatic.Telemetry;
using static Pragmatic.Ensure.Ensure;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Messaging;

/// <summary>
///     In-memory implementation of <see cref="IMessageBus"/>.
///     Dispatches messages synchronously to all registered handlers.
/// </summary>
/// <remarks>
///     Follows the same patterns as <c>InMemoryEventDispatcher</c>:
///     handler ordering, error isolation, observability, call context bypass.
///     Individual handler failures are logged but do not stop the dispatch chain.
/// </remarks>
public sealed partial class InMemoryMessageBus : IMessageBus
{
    private readonly ILogger<InMemoryMessageBus> _logger;
    private readonly IServiceProvider _serviceProvider;
    // One table per module assembly (each SG emits its own) — tried in order on dispatch.
    private readonly ITypedMessageDispatchTable[] _dispatchTables;

    public InMemoryMessageBus(
        IServiceProvider serviceProvider,
        ILogger<InMemoryMessageBus> logger,
        IEnumerable<ITypedMessageDispatchTable> dispatchTables)
    {
        ThrowIfNull(serviceProvider);
        ThrowIfNull(logger);
        ThrowIfNull(dispatchTables);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _dispatchTables = [.. dispatchTables];
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull
    {
        return PublishAsync(message, MessageContext.New(), ct);
    }

    /// <inheritdoc />
    public async Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        context = EnrichTenant(context);

        var messageType = typeof(T);
        var messageName = messageType.Name;

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity($"Message.{messageName}");
        activity?.SetTag(MessagingTags.MessageType, messageName);
        activity?.SetTag(MessagingTags.MessageId, context.MessageId);

        MessagingDiagnostics.MessagesPublished.Add(1,
            new KeyValuePair<string, object?>("message.type", messageName));

        LogPublishing(messageName, context.MessageId);

        var stopwatch = Stopwatch.StartNew();
        var allHandlers = _serviceProvider.GetServices<IMessageHandler<T>>()
            .OrderBy(h => h.Order)
            .ToList();

        // Filter by bus assignment when IBusResolver is available
        var busResolver = _serviceProvider.GetService<IBusResolver>();
        var targetBus = context.BusName;
        var handlers = busResolver is null or DefaultBusResolver
            ? allHandlers
            : allHandlers.Where(h =>
            {
                var handlerBus = busResolver.GetBusName(GetHandlerTypeName(h));
                return handlerBus == targetBus; // both null = default bus match
            }).ToList();

        if (handlers.Count == 0)
        {
            LogNoHandlers(messageName);
            activity?.SetTag(MessagingTags.HandlerCount, 0);
            activity?.SetSuccess();
            return;
        }

        activity?.SetTag(MessagingTags.HandlerCount, handlers.Count);
        LogHandlerCount(messageName, handlers.Count);

        // Enter internal call mode to bypass authorization filters (L1-L3)
        var callContext = _serviceProvider.GetService<Pragmatic.Pipeline.ICallContext>();

        var handlerFailures = 0;
        List<Exception>? failures = null;

        // Wrap every handler with the registered middleware chain so
        // features like auditing actually run. When no middleware is registered
        // the chain collapses to a direct handler call (no behaviour change).
        var middlewares = _serviceProvider.GetServices<IMessageMiddleware>()
            .OrderBy(m => m.Order)
            .ToArray();

        foreach (var handler in handlers)
        {
            var handlerName = GetHandlerDisplayName(handler);

            try
            {
                LogHandlerExecuting(messageName, handlerName);

                var scope = callContext?.EnterInternalCall();
                try
                {
                    // SG pipelines already weave the middleware chain around the handler:
                    // re-wrapping here would run every middleware twice per message.
                    MessageHandlerDelegate next = () => handler.HandleAsync(message, context, ct);
                    if (handler is not IPipelineWrappedHandler)
                    {
                        for (var i = middlewares.Length - 1; i >= 0; i--)
                        {
                            var mw = middlewares[i];
                            var inner = next;
                            next = () => mw.InvokeAsync(message, context, inner, ct);
                        }
                    }
                    await next().ConfigureAwait(false);
                }
                finally
                {
                    scope?.Dispose();
                }

                LogHandlerCompleted(messageName, handlerName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                handlerFailures++;
                (failures ??= []).Add(ex);

                MessagingDiagnostics.HandlerFailures.Add(1,
                    new KeyValuePair<string, object?>("message.type", messageName),
                    new KeyValuePair<string, object?>("handler.name", handlerName));

                LogHandlerFailed(messageName, handlerName, ex);
                activity?.RecordException(ex);
            }
        }

        stopwatch.Stop();
        MessagingDiagnostics.HandlerDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("message.type", messageName));

        if (failures is not null)
        {
            LogPublishCompletedWithFailures(messageName, handlerFailures, handlers.Count);
            activity?.SetTag(MessagingTags.HandlerFailures, handlerFailures);

            // Execution is isolated (every handler ran), but the OUTCOME must be honest:
            // swallowing here would make the transport consume path ack/commit failed
            // deliveries (dead RabbitMQ nack→DLX, dead Kafka DLQ, kill switch never trips)
            // and the in-memory outbox mark failed deliveries as delivered. Callers that
            // want fire-and-forget fan-out catch this; redelivery-scheduled failures are
            // already swallowed inside the SG pipeline and are not counted here.
            throw new AggregateException(
                $"{handlerFailures} of {handlers.Count} handler(s) failed for message {messageName}.",
                failures);
        }

        activity?.SetSuccess();
    }

    /// <inheritdoc />
    public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull
        => SendAsync(message, MessageContext.New(), ct);

    /// <inheritdoc />
    public async Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
    {
        // SendAsync is point-to-point: only one handler may receive the message.
        // Publish fan-out would silently execute every registered handler which
        // contradicts the public contract (see IMessageBus.SendAsync docs).
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        context = EnrichTenant(context);

        var handlers = _serviceProvider.GetServices<IMessageHandler<T>>()
            .OrderBy(h => h.Order)
            .ToList();

        if (handlers.Count == 0)
        {
            LogNoHandlers(typeof(T).Name);
            return;
        }

        if (handlers.Count > 1)
            LogMultipleSendHandlers(typeof(T).Name, handlers.Count);

        var handler = handlers[0];
        var callContext = _serviceProvider.GetService<Pragmatic.Pipeline.ICallContext>();
        var middlewares = _serviceProvider.GetServices<IMessageMiddleware>()
            .OrderBy(m => m.Order)
            .ToArray();

        var scope = callContext?.EnterInternalCall();
        try
        {
            // SG pipelines already weave the middleware chain — see PublishAsync.
            MessageHandlerDelegate next = () => handler.HandleAsync(message, context, ct);
            if (handler is not IPipelineWrappedHandler)
            {
                for (var i = middlewares.Length - 1; i >= 0; i--)
                {
                    var mw = middlewares[i];
                    var inner = next;
                    next = () => mw.InvokeAsync(message, context, inner, ct);
                }
            }
            await next().ConfigureAwait(false);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : notnull
        where TResponse : notnull
    {
        var handler = _serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();
        if (handler is null)
            throw new RequestReply.NoLocalRequestHandlerException(
                $"No IRequestHandler<{typeof(TRequest).Name}, {typeof(TResponse).Name}> registered.");

        return await handler.HandleAsync(request, MessageContext.New(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("AOT", "IL2026", Justification = "Dynamic fallback only used when SG-generated dispatch table is not available.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Dynamic fallback only used when SG-generated dispatch table is not available.")]
    public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default)
    {
        ThrowIfNull(message);
        ThrowIfNull(context);

        foreach (var table in _dispatchTables)
        {
            var result = table.TryDispatch(this, message, context, ct);
            if (result is not null)
                return result;
        }

        // Fallback: use dynamic dispatch when no SG-generated dispatch table
        // covers this type (e.g., passthrough default, test scenarios).
        // This routes through the DLR — not reflection — to call PublishAsync<T>.
        return PublishAsync((dynamic)message, context, ct);
    }

    /// <inheritdoc />
    public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
    {
        // In-memory bus has no transport: fan-out to in-process handlers is
        // the equivalent of "publish" here. Re-use the dispatch path.
        ThrowIfNull(message);
        ThrowIfNull(messageType);
        ThrowIfNull(context);
        return DispatchAsync(message, context, ct);
    }

    /// <summary>
    ///     Stamps the ambient tenant onto a context that has none, so a message crossing a transport
    ///     carries its originating tenant (the consumer restores it — see TransportSubscriptionBinder).
    ///     No-op when the caller already set a tenant or no tenant is resolved (single-tenant apps).
    ///     Shared with <see cref="TransportAwareMessageBus"/> via its local dispatcher so both bus
    ///     implementations capture identically without a captive scoped dependency.
    /// </summary>
    internal MessageContext EnrichTenant(MessageContext context)
    {
        if (context.TenantId is not null)
            return context;

        // Best-effort: never fail a publish because the tenant context can't be resolved — it may not
        // be registered (single-tenant), or the scope may be tearing down during shutdown. A disposed
        // provider simply means there is no live ambient tenant to capture.
        try
        {
            var tenant = _serviceProvider.GetService<ITenantContext>();
            return tenant is { IsResolved: true, TenantId: { } tenantId }
                ? context with { TenantId = tenantId }
                : context;
        }
        catch (ObjectDisposedException)
        {
            return context;
        }
    }

    /// <summary>FQN used for [OnBus] map lookups — the wrapped handler's name for SG pipelines.</summary>
    private static string GetHandlerTypeName(object handler)
        => handler is IPipelineWrappedHandler wrapped
            ? wrapped.HandlerTypeName
            : handler.GetType().FullName ?? handler.GetType().Name;

    /// <summary>Short name for logging — the wrapped handler's name for SG pipelines.</summary>
    private static string GetHandlerDisplayName(object handler)
    {
        if (handler is not IPipelineWrappedHandler wrapped)
            return handler.GetType().Name;

        var fqn = wrapped.HandlerTypeName;
        var lastDot = fqn.LastIndexOf('.');
        return lastDot >= 0 ? fqn[(lastDot + 1)..] : fqn;
    }

    // =========================================================================
    // LoggerMessage - Zero Allocation Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Publishing message {MessageName} (id: {MessageId})")]
    private partial void LogPublishing(string messageName, string messageId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "No handlers registered for message {MessageName}")]
    private partial void LogNoHandlers(string messageName);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Found {HandlerCount} handler(s) for message {MessageName}")]
    private partial void LogHandlerCount(string messageName, int handlerCount);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Executing handler {HandlerName} for message {MessageName}")]
    private partial void LogHandlerExecuting(string messageName, string handlerName);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Handler {HandlerName} completed for message {MessageName}")]
    private partial void LogHandlerCompleted(string messageName, string handlerName);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Handler {HandlerName} failed for message {MessageName}")]
    private partial void LogHandlerFailed(string messageName, string handlerName, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Publish completed for message {MessageName} with {FailureCount}/{TotalCount} handler failure(s)")]
    private partial void LogPublishCompletedWithFailures(string messageName, int failureCount, int totalCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "SendAsync for {MessageName} has {HandlerCount} registered handlers — only the first (ordered) will run (point-to-point semantics).")]
    private partial void LogMultipleSendHandlers(string messageName, int handlerCount);
}
