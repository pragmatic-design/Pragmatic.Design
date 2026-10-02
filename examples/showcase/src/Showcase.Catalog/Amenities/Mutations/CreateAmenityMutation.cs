namespace Showcase.Catalog.Amenities.Mutations;

/// <summary>
/// Creates a new amenity.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation] + [Endpoint] + [InvalidatesCache] with enum property.
/// </summary>
[Mutation(Mode = MutationMode.Create)]
[InvalidatesCache("amenities")]
[RequirePermission(CatalogPermissions.Amenity.Create)]
[Endpoint(HttpVerb.Post, "api/amenities")]
[ReturnsDto<AmenityDto>]
public partial class CreateAmenityMutation : Mutation<Amenity>
{
    public required string Name { get; init; }
    public AmenityCategory Category { get; init; }
    public string? IconName { get; init; }

    /// <summary>Search keywords stored as a JSON primitive collection on the entity.</summary>
    public List<string>? Keywords { get; init; }
}
