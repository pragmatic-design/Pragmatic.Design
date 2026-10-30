using Pragmatic.Events;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>An order, on the Sql engine. Placing it raises <see cref="OrderPlaced" />.</summary>
[Entity]
[BelongsTo<OrdersBoundary>]
public partial class Order : IEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    public string Number { get; set; } = "";

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    public void ClearDomainEvents() => _events.Clear();

    public void Place() => _events.Add(new OrderPlaced(Number, DateTimeOffset.UnixEpoch));
}
