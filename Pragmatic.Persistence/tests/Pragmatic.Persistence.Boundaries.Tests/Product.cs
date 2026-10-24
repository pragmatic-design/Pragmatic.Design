using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>A product of the catalog: soft-deleted, owned by the user who listed it.</summary>
[Entity]
[BelongsTo<CatalogBoundary>]
[SoftDelete]
[HasOwner]
public partial class Product : IEntity
{
    public string Name { get; set; } = "";
}
