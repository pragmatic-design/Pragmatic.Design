using Pragmatic.Mapping;

namespace Showcase.Catalog.Amenities.Actions;

/// <summary>
/// Reads an amenity by its domain key — the <c>[LogicKey]</c> on <c>Amenity.Name</c>.
///
/// Demonstrates: <c>[LoadEntity(By = …)]</c>. The invoker reads the row through the accessor the domain key
/// generates — <c>AmenitySpecifications.GetByNameAsync</c>, an extension on <c>IReadRepository&lt;Amenity&gt;</c>
/// that delegates to the <c>ByName</c> specification, so the key's columns are written once — and answers 404
/// for a name no row carries. The action only reads what it was handed.
///
/// ⚠️ Not the repository injected by hand: a field of the concrete <c>Amenity.Repository</c> is
/// PRAG0419, and a load by key written out answers the 404 by hand.
/// </summary>
[DomainAction]
[Endpoint(HttpVerb.Get, "api/amenities/by-name/{name}")]
[ApiSummary("Get Amenity By Name")]
[ApiTags("Amenities")]
[RequirePermission(CatalogPermissions.Amenity.Read)]
[LoadEntity<Amenity>(nameof(Name), By = nameof(Amenity.Name))]
public partial class GetAmenityByNameAction : DomainAction<AmenityDto, NotFoundError>
{
    /// <summary>The amenity's domain key.</summary>
    public required string Name { get; init; }

    public override Task<Result<AmenityDto, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<AmenityDto, IError>>(AmenityDto.FromEntity(_amenity));
}
