using Conformance.Catalog.Entities;
using Pragmatic.Mapping.Attributes;

namespace Conformance.Catalog.Dtos;

/// <summary>
///     The category, in the shape the <b>read contract</b> hands to another boundary.
/// </summary>
/// <remarks>
///     It is not a route's shape: no endpoint publishes it. It is what <c>ICatalogReads</c> returns, and
///     so it carries nothing another boundary must not see.
/// </remarks>
[MapFrom<Category>]
[GenerateProjection]
public partial record CategoryDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";
}
