using Pragmatic.Events;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>A shipment, on the documents engine. Sending it raises <see cref="ShipmentSent" />.</summary>
[Entity]
[BelongsTo<ShipmentsBoundary>]
public partial class Shipment : IEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    public string Carrier { get; set; } = "";

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    public void ClearDomainEvents() => _events.Clear();

    public void Send() => _events.Add(new ShipmentSent(Carrier, DateTimeOffset.UnixEpoch));
}
