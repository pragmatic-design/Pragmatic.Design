using Conformance.Catalog.Entities;
using Pragmatic.Mapping.Attributes;

namespace Conformance.Catalog.Dtos;

/// <summary>
///     The item, read-only: the shape <c>SearchCatalogItemsQuery</c> puts on the wire.
/// </summary>
[MapFrom<CatalogItem>]
[GenerateProjection]
public partial record CatalogItemDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    public decimal ListPrice { get; init; }
}
