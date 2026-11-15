using System.Text.Json;
using Pragmatic.Events.Outbox;
using Pragmatic.Serialization;

namespace Pragmatic.Events.Engines;

/// <summary>
///     What both engines' phases take from a save: the events of the entities it writes, and the events the operation declared
///     (<see cref="DeclaredEventStage" />), each in the form the outbox stores (<see cref="EventOutboxDelivery.Capture" />). The entities'
///     events are cleared and the stage emptied, so neither the unit of work nor the invoker, which dispatch after the commit, finds any
///     left: an event goes out once, through the outbox.
/// </summary>
internal static class EventOutboxCapture
{
    /// <summary>
    ///     The events of <paramref name="entities" /> and those <paramref name="stage" /> holds, taken: empty when there are none (and the
    ///     JSON options are not built).
    /// </summary>
    public static List<CapturedEvent> Take(
        IEnumerable<object> entities, DeclaredEventStage? stage, DateTimeOffset now, string? tenantId, PragmaticJsonOptions? json)
    {
        List<CapturedEvent>? captured = null;
        JsonSerializerOptions? options = null;
        var traceParent = System.Diagnostics.Activity.Current?.Id;

        void Add(IDomainEvent domainEvent)
        {
            options ??= (json ?? PragmaticJsonOptions.Default).Build();
            captured ??= [];
            var (eventType, payload) = EventOutboxDelivery.Capture(domainEvent, options);
            captured.Add(new CapturedEvent(Guid.NewGuid(), eventType, payload, domainEvent.OccurredAt.UtcTicks, tenantId, traceParent, now.UtcTicks));
        }

        foreach (var entity in entities)
        {
            if (entity is not IHasDomainEvents { DomainEvents.Count: > 0 } raiser)
                continue;
            foreach (var domainEvent in raiser.DomainEvents)
                Add(domainEvent);
            raiser.ClearDomainEvents();
        }

        // Built here, inside the save, after the generated values are filled: what a declared event reads of the entity is what is saved.
        if (stage is not null)
        {
            foreach (var domainEvent in stage.Take())
                Add(domainEvent);
        }

        return captured ?? [];
    }
}
