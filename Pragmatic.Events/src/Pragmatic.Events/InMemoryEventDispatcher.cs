using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Events.Diagnostics;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Events;

/// <summary>
///     In-memory implementation of <see cref="IDomainEventDispatcher" />.
///     Dispatches events synchronously to all registered handlers.
/// </summary>
/// <remarks>
///     <para>
///         This implementation is suitable for:
///     </para>
///     <list type="bullet">
///         <item>Development and testing scenarios</item>
///         <item>Simple applications without distributed requirements</item>
///         <item>Side effects that must occur within the same transaction</item>
///     </list>
///     <para>
///         Individual handler failures are logged and recorded but do not stop
///         the dispatch chain. All registered handlers will be invoked regardless
///         of failures in other handlers. For production systems requiring strict
///         ordering guarantees, use an outbox pattern with a message broker.
///     </para>
/// </remarks>
public sealed partial class InMemoryEventDispatcher : IDomainEventDispatcher
{
    private readonly ILogger<InMemoryEventDispatcher> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITypedEventDispatchTable[] _dispatchTables;

    /// <summary>
    ///     Creates a new in-memory event dispatcher.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving handlers.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="dispatchTables">
    ///     SG-generated dispatch tables for zero-reflection dispatch — one per composed module.
    ///     Probed in order; the first that recognises the event type wins. Empty when no module
    ///     references the source generator, in which case the untyped path uses a dynamic fallback.
    /// </param>
    public InMemoryEventDispatcher(
        IServiceProvider serviceProvider,
        ILogger<InMemoryEventDispatcher> logger,
        IEnumerable<ITypedEventDispatchTable> dispatchTables)
    {
        ThrowIfNull(serviceProvider);
        ThrowIfNull(logger);
        ThrowIfNull(dispatchTables);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _dispatchTables = dispatchTables as ITypedEventDispatchTable[] ?? dispatchTables.ToArray();
    }

    /// <inheritdoc />
    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IDomainEvent
    {
        var eventType = typeof(TEvent);
        var eventName = eventType.Name;

        using var activity = EventsDiagnostics.ActivitySource.StartActivity($"Event.{eventName}");
        activity?.SetTag(EventTags.Name, eventName);

        EventsDiagnostics.EventsDispatched.Add(1,
            new KeyValuePair<string, object?>("event.name", eventName));

        LogDispatching(eventName);

        var stopwatch = Stopwatch.StartNew();
        // Materialise first; sort only when there are multiple handlers to avoid
        // an extra allocation and sort pass on the common single-handler path.
        var handlers = _serviceProvider.GetServices<IDomainEventHandler<TEvent>>().ToList();
        if (handlers.Count > 1)
            handlers.Sort(static (a, b) => a.Order.CompareTo(b.Order));

        if (handlers.Count == 0)
        {
            LogNoHandlers(eventName);
            activity?.SetTag(EventTags.HandlerCount, 0);
            activity?.SetSuccess();
            return;
        }

        activity?.SetTag(EventTags.HandlerCount, handlers.Count);
        LogHandlerCount(eventName, handlers.Count);

        var handlerFailures = 0;

        // Event handlers are system reactions to domain events — they should not be
        // blocked by the HTTP user's permissions. Enter internal call mode so that
        // authorization filters (L1 permission, L2 policy, L3 ABAC) skip checks.
        // Data-level filters (L4) remain active — the user context is preserved.
        // ICallContext is optional: if Pragmatic.Actions is not referenced, handlers
        // run without call context (no auth filters exist either).
        var callContext = _serviceProvider.GetService<Pragmatic.Pipeline.ICallContext>();

        foreach (var handler in handlers)
        {
            var handlerName = handler.GetType().Name;

            try
            {
                LogHandlerExecuting(eventName, handlerName);

                var scope = callContext?.EnterInternalCall();
                try
                {
                    await handler.HandleAsync(@event, ct).ConfigureAwait(false);
                }
                finally
                {
                    scope?.Dispose();
                }

                LogHandlerCompleted(eventName, handlerName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log failure but continue to next handler
                handlerFailures++;

                EventsDiagnostics.HandlerFailures.Add(1,
                    new KeyValuePair<string, object?>("event.name", eventName),
                    new KeyValuePair<string, object?>("handler.name", handlerName));

                LogHandlerFailed(eventName, handlerName, ex);
                activity?.RecordException(ex);
            }
        }

        stopwatch.Stop();
        EventsDiagnostics.DispatchDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("event.name", eventName));

        if (handlerFailures > 0)
        {
            LogDispatchCompletedWithFailures(eventName, handlerFailures, handlers.Count);
            activity?.SetTag(EventTags.HandlerFailures, handlerFailures);
        }
        else
        {
            // Only log the success summary when all handlers succeeded to avoid
            // emitting two log entries (CompletedWithFailures + Completed) for the same dispatch.
            LogDispatchCompleted(eventName);
            activity?.SetSuccess();
        }
    }

    /// <inheritdoc />
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
    {
        foreach (var @event in events)
        {
            ct.ThrowIfCancellationRequested();
            await DispatchEventAsync(@event, ct).ConfigureAwait(false);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2026", Justification = "Dynamic fallback only used when SG-generated dispatch table is not available.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Dynamic fallback only used when SG-generated dispatch table is not available.")]
    private Task DispatchEventAsync(IDomainEvent @event, CancellationToken ct)
    {
        // Probe each SG-generated typed table (one per composed module) in order.
        // The first that recognises the concrete event type routes it to the strongly
        // typed DispatchAsync<TEvent> via compile-time pattern matching — AOT-safe, no DLR.
        foreach (var table in _dispatchTables)
        {
            var result = table.TryDispatch(this, @event, ct);
            if (result is not null)
                return result;
        }

        // Fallback: dynamic dispatch when no SG-generated table covers this event type
        // (no module references the source generator, or an event dispatched with no handler).
        // Routes through the DLR — not reflection — but is NOT AOT-safe; see ITypedEventDispatchTable.
        return DispatchAsync((dynamic)@event, ct);
    }

    // =========================================================================
    // LoggerMessage - Zero Allocation Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Dispatching domain event {EventName}")]
    private partial void LogDispatching(string eventName);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "No handlers registered for event {EventName}")]
    private partial void LogNoHandlers(string eventName);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Found {HandlerCount} handler(s) for event {EventName}")]
    private partial void LogHandlerCount(string eventName, int handlerCount);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Executing handler {HandlerName} for event {EventName}")]
    private partial void LogHandlerExecuting(string eventName, string handlerName);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Handler {HandlerName} completed for event {EventName}")]
    private partial void LogHandlerCompleted(string eventName, string handlerName);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Handler {HandlerName} failed for event {EventName}")]
    private partial void LogHandlerFailed(string eventName, string handlerName, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Dispatch completed for event {EventName} with {FailureCount}/{TotalCount} handler failure(s)")]
    private partial void LogDispatchCompletedWithFailures(string eventName, int failureCount, int totalCount);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Dispatch completed for event {EventName}")]
    private partial void LogDispatchCompleted(string eventName);
}
