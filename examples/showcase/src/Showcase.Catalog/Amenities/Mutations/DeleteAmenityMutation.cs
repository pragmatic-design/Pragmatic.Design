namespace Showcase.Catalog.Amenities.Mutations;

/// <summary>
/// Soft-deletes an amenity.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation(Mode=Delete)] + [Endpoint] for soft-delete.
/// </summary>
[Mutation(Mode = MutationMode.Delete)]
[Endpoint(HttpVerb.Delete, "api/amenities/{id}")]
[RequirePermission(CatalogPermissions.Amenity.Delete)]
public partial class DeleteAmenityMutation : Mutation<Amenity>
{
    public required Guid Id { get; init; }
}
