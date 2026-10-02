namespace Showcase.Catalog.Amenities.Mutations;

/// <summary>
/// Updates an amenity.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation] + [Endpoint] with enum property, and the implicit id —
/// an Update addresses an existing row, so the generator writes the <c>Id</c> and the route parameter
/// binds to it. Declaring your own still wins.
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[Endpoint(HttpVerb.Put, "api/amenities/{id}")]
[RequirePermission(CatalogPermissions.Amenity.Update)]
public partial class UpdateAmenityMutation : Mutation<Amenity>
{
    public string? Name { get; init; }
    public AmenityCategory? Category { get; init; }
    public string? IconName { get; init; }
}
