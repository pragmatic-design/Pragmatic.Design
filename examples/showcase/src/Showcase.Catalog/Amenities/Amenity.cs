namespace Showcase.Catalog.Entities;

/// <summary>
/// An amenity that can be offered by hotel properties (e.g. WiFi, Pool, Spa).
/// </summary>
[Entity]
[SoftDelete]
[Audited]
[Relation.ManyToMany<Property>.WithNavigation("Properties", Inverse = "Amenities")]
[Relation.ManyToMany<RoomType, RoomTypeAmenity>.WithNavigation("RoomTypes")]
public partial class Amenity : IEntity
{
    [Required]
    [LogicKey]
    [Autocomplete]
    public string Name { get; private set; } = "";

    public AmenityCategory Category { get; private set; }

    public string IconName { get; private set; } = "";

    /// <summary>
    /// Free-form search keywords (e.g. "wifi", "wireless", "internet"). Collection of primitives —
    /// EF Core persists this as a JSON column, not a navigation. Exercises the SG primitive-collection path.
    /// </summary>
    public List<string> Keywords { get; private set; } = [];

    /// <summary>
    /// Support contact — a [ValueObject] persisted as an EF Core complex type (Support_Email /
    /// Support_Phone columns). Non-null default so existing creates that omit it remain valid.
    /// </summary>
    public ContactInfo Support { get; private set; } = new("", "");

    /// <summary>
    /// Aggregate invariant: an amenity may carry at most 20 search keywords. Enforced by the generated
    /// mutation invoker after apply and before persist — a violating mutation is rejected (422), so the
    /// invalid state never reaches the database. Exercises the SG [Invariant] feature end-to-end.
    /// </summary>
    [Invariant("An amenity cannot have more than 20 keywords")]
    public bool KeywordsWithinLimit() => Keywords.Count <= 20;

}
