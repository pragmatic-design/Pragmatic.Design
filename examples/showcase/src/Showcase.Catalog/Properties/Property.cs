using Showcase.Catalog.Properties;

namespace Showcase.Catalog.Entities;

/// <summary>
/// A hotel property (the hotel itself).
/// </summary>
[Entity]
[Auditable]
[SoftDelete(Cascade = true)]
// A deactivated property is out of the catalogue, for every read. Repeated at each call site
// with .Where(PropertySpecifications.IsActive()), every read that forgot it would show them.
[VisibleWhen<ActiveOnly>]
[Relation.OneToMany<RoomType>]
[Relation.OneToMany<CancellationPolicy>]
[Relation.ManyToMany<Amenity>.WithNavigation("Amenities", Inverse = "Properties", JoinTable = "PropertyAmenities")]
[Relation.ManyToOne<Category>.WithNavigation("Category", Required = false)]
public partial class Property : IEntity, ITenantEntity
{
    [Required]
    [LogicKey]
    public string Code { get; private set; } = "";

    [Required]
    [Autocomplete<PropertySummaryDto>]
    public string Name { get; private set; } = "";

    /// <summary>
    ///     Multi-language description. Use <c>.Value</c> for the current culture.
    /// </summary>
    /// <remarks>
    ///     Stored as JSON in one column, and the generated entity configuration is what converts it:
    ///     <c>EntityConfig.…Property.g.cs</c> emits the conversion inline. ⚠️ There is nothing to call
    ///     in a DbContext. <c>ApplyPragmaticInternationalization()</c> is for an application writing its
    ///     own <c>OnModelCreating</c>, which is not what a Pragmatic module does.
    /// </remarks>
    public LocalizedString Description { get; private set; } = new();

    public string Address { get; private set; } = "";

    [Required]
    [Autocomplete]
    public string City { get; private set; } = "";

    [Required]
    public string Country { get; private set; } = "";

    [Range(1, 5)]
    public int StarRating { get; private set; }

    /// <summary>IANA timezone identifier (e.g. "Europe/Rome").</summary>
    public string TimeZone { get; private set; } = "UTC";

    public TimeOnly CheckInTime { get; private set; } = new(14, 0);

    public TimeOnly CheckOutTime { get; private set; } = new(11, 0);

    public bool IsActive { get; private set; } = true;


    // ITenantEntity — auto-set by TenantInterceptor on insert, filtered by TenantFilter
    public string TenantId { get; set; } = "";

}
