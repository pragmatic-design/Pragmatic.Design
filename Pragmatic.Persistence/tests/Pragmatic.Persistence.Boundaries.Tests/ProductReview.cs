using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     A review of a product, in the other boundary and on the other engine: the relation crosses the boundary, so only its key,
///     <c>ProductId</c>, is generated (no navigation without <c>[ReadAccess&lt;Product&gt;]</c>).
/// </summary>
[Entity]
[BelongsTo<ReviewsBoundary>]
[HasOwner]
[Relation.ManyToOne<Product>]
public partial class ProductReview : IEntity
{
    public string Text { get; set; } = "";

    public int Stars { get; set; }
}
