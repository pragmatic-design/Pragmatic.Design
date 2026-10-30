using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Pragmatic.Events;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging.Entities;

/// <summary>
///     What every outbox capture takes from a save, whatever store it writes to: the domain events of the entities the save writes, each an
///     <see cref="OutboxMessage" /> carrying the originating tenant, user and correlation, and the entities' events cleared, so nothing after the
///     save dispatches them a second time.
/// </summary>
/// <remarks>
///     EF Core's <c>OutboxInterceptor</c> and the engines' save phases call it with the scoped services of the save. Without the ambient context
///     the delivery pump would rebuild a <see cref="MessageContext" /> with a null tenant and user, and multi-tenancy and tracing would break.
/// </remarks>
public static class OutboxMessageCapture
{
    /// <summary>
    ///     The events of <paramref name="entities" />, taken as outbox messages: empty when none raised one (and the JSON options are not built).
    /// </summary>
    public static List<OutboxMessage> Take(
        IEnumerable<object> entities,
        DateTimeOffset now,
        ITenantContext? tenant,
        ICurrentUser? currentUser,
        PragmaticJsonOptions? json)
    {
        ArgumentNullException.ThrowIfNull(entities);

        List<OutboxMessage>? messages = null;
        JsonSerializerOptions? serializerOptions = null;
        AmbientContext ambient = default;

        foreach (var entity in entities)
        {
            if (entity is not IHasDomainEvents { DomainEvents.Count: > 0 } raiser)
                continue;

            if (serializerOptions is null)
            {
                // The shared JSON options from the seam (AOT-safe contexts when configured), falling back to the reflection-based default.
                serializerOptions = (json ?? PragmaticJsonOptions.Default).Build();
                // Captured ONCE per save, so every row carries the same originating tenant/user/correlation.
                ambient = CaptureAmbientContext(tenant, currentUser, serializerOptions);
                messages = [];
            }

            foreach (var domainEvent in raiser.DomainEvents)
            {
                var eventType = domainEvent.GetType();
                messages!.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    MessageType = eventType.FullName ?? eventType.Name,
                    // Serialize through the resolver's JsonTypeInfo (AOT-safe: the SG-generated JsonSerializerContext carries every domain-event
                    // type). Symmetric with the read path, which deserializes via the SG IMessageTypeRegistry. No reflection fallback.
                    Payload = JsonSerializer.Serialize(domainEvent, serializerOptions.GetTypeInfo(eventType)),
                    CreatedAt = now,
                    RetryCount = 0,
                    CorrelationId = ambient.CorrelationId,
                    TenantId = ambient.TenantId,
                    UserId = ambient.UserId,
                    HeadersJson = ambient.HeadersJson,
                });
            }

            // Clear events from the entity to prevent re-dispatch.
            raiser.ClearDomainEvents();
        }

        return messages ?? [];
    }

    /// <summary>Tenant / user / correlation of the save in progress. Correlation falls back to the ambient <see cref="Activity" />.</summary>
    private static AmbientContext CaptureAmbientContext(ITenantContext? tenant, ICurrentUser? currentUser, JsonSerializerOptions serializerOptions)
    {
        var activity = Activity.Current;
        var correlationId = activity?.GetBaggageItem("correlation.id")
            ?? activity?.Id
            ?? activity?.TraceId.ToString();

        // Tenant precedence: explicit tenant context → user's tenant claim.
        var tenantId = tenant?.TenantId ?? currentUser?.TenantId;

        var headersJson = BuildHeadersJson(activity, serializerOptions);

        return new AmbientContext(correlationId, tenantId, currentUser?.IdOrNull(), headersJson);
    }

    [UnconditionalSuppressMessage("AOT", "IL2026", Justification = "Header bag is a dictionary of strings; AOT apps use SG-generated TypeRegistry for the message payload.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Header bag is a dictionary of strings; AOT apps use SG-generated TypeRegistry for the message payload.")]
    private static string? BuildHeadersJson(Activity? activity, JsonSerializerOptions serializerOptions)
    {
        if (activity is null)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in activity.Baggage)
        {
            if (value is not null)
                headers[key] = value;
        }

        if (activity.TraceId != default)
            headers["traceparent"] = activity.Id ?? activity.TraceId.ToString();

        return headers.Count == 0 ? null : JsonSerializer.Serialize(headers, serializerOptions);
    }

    private readonly record struct AmbientContext(
        string? CorrelationId,
        string? TenantId,
        string? UserId,
        string? HeadersJson);
}
