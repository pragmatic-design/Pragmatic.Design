using System.Text.Json;
using Pragmatic.Events.Outbox;
using Pragmatic.Serialization;

namespace Pragmatic.Events.Engines;

/// <summary>
///     What both engines' phases take from a save: the events of the entities it writes, each in the form the outbox stores
///     (<see cref="EventOutboxDelivery.Capture" />), the entities' events cleared, so the unit of work, which publishes after the commit, finds none
///     left: an event goes out once, through the outbox.
/// </summary>
internal static class EventOutboxCapture
{
    /// <summary>The events of <paramref name="entities" />, taken: empty when none raised one (and the JSON options are not built).</summary>
    public static List<CapturedEvent> Take(IEnumerable<object> entities, DateTimeOffset now, string? tenantId, PragmaticJsonOptions? json)
    {
        List<CapturedEvent>? captured = null;
        JsonSerializerOptions? options = null;
        var traceParent = System.Diagnostics.Activity.Current?.Id;
        foreach (var entity in entities)
        {
            if (entity is not IHasDomainEvents { DomainEvents.Count: > 0 } raiser)
                continue;
            options ??= (json ?? PragmaticJsonOptions.Default).Build();
            captured ??= [];
            foreach (var domainEvent in raiser.DomainEvents)
            {
                var (eventType, payload) = EventOutboxDelivery.Capture(domainEvent, options);
                captured.Add(new CapturedEvent(Guid.NewGuid(), eventType, payload, domainEvent.OccurredAt.UtcTicks, tenantId, traceParent, now.UtcTicks));
            }

            raiser.ClearDomainEvents();
        }

        return captured ?? [];
    }
}
