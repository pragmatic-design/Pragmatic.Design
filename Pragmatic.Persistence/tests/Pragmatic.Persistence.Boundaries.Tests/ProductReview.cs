using Pragmatic.Events;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     A review of a product, in the other boundary and on the other engine: the relation crosses the boundary, so only its key,
///     <c>ProductId</c>, is generated (no navigation without <c>[ReadAccess&lt;Product&gt;]</c>). Posting it raises <see cref="ProductReviewed" />.
/// </summary>
[Entity]
[BelongsTo<ReviewsBoundary>]
[HasOwner]
[Relation.ManyToOne<Product>]
public partial class ProductReview : IEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    public string Text { get; set; } = "";

    public int Stars { get; set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    public void ClearDomainEvents() => _events.Clear();

    public void Post() => _events.Add(new ProductReviewed(Text, Stars, DateTimeOffset.UnixEpoch));
}
