namespace Showcase.Catalog.Dtos;

/// <summary>
/// Lightweight property representation for list views.
/// </summary>
[MapFrom<Property>]
[GenerateProjection]
public partial class PropertySummaryDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string City { get; init; } = "";
    public string Country { get; init; } = "";
    public int StarRating { get; init; }
    public bool IsActive { get; init; }
}
