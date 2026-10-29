using System.Diagnostics;
using System.Text.Json;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Events.Outbox;

/// <summary>
///     What every event outbox does with one event, whatever stores its rows: the form an event is captured in, and the delivery of a
///     captured row (the event resolved through the allowlist, read back, dispatched under the tenant and inside the trace it was raised in).
///     The stores (EF Core's, the engines') claim, load and mark the rows; this is the part they share.
/// </summary>
public static class EventOutboxDelivery
{
    /// <summary>Trace source for outbox delivery spans; links back to the originating request trace.</summary>
    /// <remarks>The source's <em>name</em> is what observability configuration keys on.</remarks>
    public static readonly ActivitySource ActivitySource = new("Pragmatic.Events.Outbox");

    /// <summary>
    ///     The event type and payload an outbox row holds for <paramref name="domainEvent" />: the type's assembly-qualified name, which the
    ///     allowlist resolves, and the event serialized through <paramref name="options" /> (the generated JSON context: no reflection).
    /// </summary>
    public static (string EventType, string Payload) Capture(IDomainEvent domainEvent, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(options);
        var eventType = domainEvent.GetType();
        return (eventType.AssemblyQualifiedName ?? eventType.FullName ?? eventType.Name, JsonSerializer.Serialize(domainEvent, options.GetTypeInfo(eventType)));
    }

    /// <summary>
    ///     Delivers one captured event: null when it was dispatched, the reason when it cannot be (its type is not allowlisted, its payload is
    ///     not an event). A handler's failure propagates, for the store to record against the row.
    /// </summary>
    public static async Task<string?> DeliverAsync(string eventType, string payload, string? tenantId, string? traceParent,
        IEventOutboxTypeResolver types, JsonSerializerOptions options, IDomainEventDispatcher dispatcher, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dispatcher);

        // Fail-closed: only allowlisted (registered-handler) event types resolve.
        var type = types.Resolve(eventType);
        if (type is null)
        {
            // The allowlist is built from registered IDomainEventHandler<T> types, so a miss means
            // no handler is registered for this event (or the type was renamed/moved since it was written).
            return $"No registered handler for event type '{eventType}' — the outbox deserialization " +
                   "allowlist is derived from IDomainEventHandler<T> registrations. Register a handler, or the type was renamed.";
        }

        if (JsonSerializer.Deserialize(payload, options.GetTypeInfo(type)) is not IDomainEvent domainEvent)
            return "Payload did not deserialize to an IDomainEvent.";

        // Re-attach to the originating distributed trace so handler work shows under the
        // request that raised the event, across the async outbox boundary.
        using var activity = StartDeliveryActivity(traceParent);

        // Restore the originating tenant so tenant-scoped queries inside handlers resolve the
        // correct filter instead of the worker's null tenant.
        // ⚠️ TenantScope, not IMutableTenantContext.SetTenant. Five workers restored the tenant by
        // hand, each resolving the mutable context from its own scope, while the mechanism the
        // documentation points at had zero callers in the framework — because it could not be
        // called: it lived in Pragmatic.MultiTenancy and these modules only reference
        // Abstractions. Moving it beside the contract it serves is what made one way possible.
        // The registered ITenantContext is AmbientTenantContext: it prefers the request tenant
        // and falls back to this ambient one, and outside a request there is no request tenant.
        using (tenantId is { Length: > 0 } ? TenantScope.BeginScope(tenantId) : null)
        {
            await dispatcher.DispatchAsync([domainEvent], ct).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    ///     Starts a delivery span, parented to the W3C trace captured at write time when present, so a
    ///     distributed trace spans the asynchronous outbox boundary. Returns null when no listener is
    ///     registered (zero overhead) — the dispatch still runs.
    /// </summary>
    private static Activity? StartDeliveryActivity(string? traceParent)
        => !string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, null, out var parent)
            ? ActivitySource.StartActivity("EventOutbox.Deliver", ActivityKind.Consumer, parent)
            : ActivitySource.StartActivity("EventOutbox.Deliver", ActivityKind.Consumer);
}
