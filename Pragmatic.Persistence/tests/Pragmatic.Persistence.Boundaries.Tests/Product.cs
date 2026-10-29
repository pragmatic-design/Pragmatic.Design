using Pragmatic.Events;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>A product of the catalog: soft-deleted, owned by the user who listed it. Listing it raises <see cref="ProductListed" />.</summary>
[Entity]
[BelongsTo<CatalogBoundary>]
[SoftDelete]
[HasOwner]
public partial class Product : IEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    public string Name { get; set; } = "";

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    public void ClearDomainEvents() => _events.Clear();

    public void List() => _events.Add(new ProductListed(Name, DateTimeOffset.UnixEpoch));
}
