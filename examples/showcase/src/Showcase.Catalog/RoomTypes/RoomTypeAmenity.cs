namespace Showcase.Catalog.Entities;

/// <summary>
/// Join entity for the RoomType ↔ Amenity many-to-many relationship.
/// Demonstrates ManyToMany with explicit join entity carrying payload fields.
/// </summary>
/// <remarks>
/// The two ManyToOne relations are what make the join usable: they generate the foreign keys, and the
/// many-to-many declarations on either side bind to them. Without them EF Core invented shadow keys
/// named after the navigations, which the migration never created — the table existed with the
/// payload columns alone and the first write through the navigation would have failed on a column
/// that does not exist. Nothing noticed because nothing ever wrote through it (PRAG0616).
/// </remarks>
[Entity]
// Soft-delete, because both ends are: a required navigation to a soft-deleted row is an INNER JOIN
// that hides the join row from every query without deleting it, and a row that is invisible and not
// restorable is worse than one that is gone (PRAG0705).
[SoftDelete]
[Relation.ManyToOne<RoomType>]
[Relation.ManyToOne<Amenity>]
public partial class RoomTypeAmenity : IEntity
{
    /// <summary>Whether the amenity is included in the base room rate.</summary>
    public bool IsIncluded { get; private set; } = true;

    /// <summary>Additional cost if the amenity is not included in the base rate.</summary>
    public decimal AdditionalCost { get; private set; }

    /// <summary>ISO 4217 currency code for the additional cost.</summary>
    public string Currency { get; private set; } = "EUR";
}
