namespace Showcase.Catalog.Dtos;

/// <summary>
/// Amenity DTO for display and selection.
/// </summary>
[MapFrom<Amenity>]
[GenerateProjection]
public partial class AmenityDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public AmenityCategory Category { get; init; }
    public string IconName { get; init; } = "";

    /// <summary>Search keywords — primitive collection mapped straight through from the entity.</summary>
    public List<string> Keywords { get; init; } = [];
}
