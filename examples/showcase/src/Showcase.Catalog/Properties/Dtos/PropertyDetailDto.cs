namespace Showcase.Catalog.Dtos;

/// <summary>
/// Full property details for single-property views.
/// Demonstrates: MapFrom, MapProperty(Format), MapIgnore.
/// </summary>
[MapFrom<Property>]
[GenerateProjection]
public partial class PropertyDetailDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Description in current culture (implicit LocalizedString → string conversion).</summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// All available translations, which is what an edit screen needs — <c>Description</c> above answers
    /// the current culture, which is what a page shows.
    /// </summary>
    /// <remarks>
    /// ⚠️ It was <c>[MapIgnore]</c> and filled by nobody, so every property read published
    /// <c>"descriptions": null</c> while its comment claimed to demonstrate full access to the type. The
    /// name does not match the entity's (<c>Description</c>), which is why the mapping skipped it and why
    /// <c>[MapProperty]</c> is what makes it real.
    /// </remarks>
    [MapProperty("Description")]
    public LocalizedString? Descriptions { get; init; }
    public string Address { get; init; } = "";
    public string City { get; init; } = "";
    public string Country { get; init; } = "";
    public int StarRating { get; init; }

    /// <summary>Operational detail exposed only for active properties. Demonstrates [MapCondition]:
    /// the property maps only when the predicate returns true, otherwise it keeps its default.
    /// Applies to FromEntity only — the SQL projection maps it unconditionally (PRAG0332 Info).</summary>
    [MapCondition(nameof(ShouldMapOperationalDetails))]
    public string TimeZone { get; init; } = "";

    private static bool ShouldMapOperationalDetails(Property source) => source.IsActive;
    public TimeOnly CheckInTime { get; init; }
    public TimeOnly CheckOutTime { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Formatted creation date (yyyy-MM-dd). Demonstrates MapProperty(Format).
    /// Uses string literal because CreatedAt is trait-generated (not visible to nameof at SG time).</summary>
    [MapProperty("CreatedAt", Format = "yyyy-MM-dd")]
    public string CreatedDate { get; init; } = "";

    /// <summary>Computed display name — not mapped from entity.</summary>
    [MapIgnore]
    public string DisplayLabel => $"{Name} ({City}, {StarRating}★)";

    /// <summary>
    /// Description in current culture, with localized fallback when empty.
    /// Demonstrates: T.Property.NoDescription — embedded LocalizedString from translations/*.json.
    /// </summary>
    [MapIgnore]
    public string DescriptionOrPlaceholder =>
        string.IsNullOrEmpty(Description) ? T.Property.NoDescription.Value : Description;
}
