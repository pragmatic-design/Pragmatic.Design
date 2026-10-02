namespace Showcase.Catalog.Amenities.Queries;

/// <summary>
/// Reads one amenity by id through the query pipeline.
/// </summary>
/// <remarks>
/// <para>
///     <c>Single = true</c>: the generated endpoint calls <c>ExecuteSingleAsync</c> and answers 404
///     when nothing matches, instead of 200 with an empty list. It is the declarative form of reading one
///     record, so a get-by-id stays in the query pipeline instead of becoming a repository call inside
///     a hand-written endpoint.
/// </para>
/// <para>
///     It is here, and not only in a generator test, because the endpoint generator tests cannot
///     assert that what they emit compiles — their reference set is incomplete. The
///     gate builds the Showcase for real, so this file is the compilation proof.
/// </para>
/// </remarks>
[Query<Amenity, AmenityDto>(Single = true)]
[Endpoint(HttpVerb.Get, "api/amenities/by-id/{id}")]
[RequirePermission(CatalogPermissions.Amenity.Read)]
public partial class GetAmenityQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
