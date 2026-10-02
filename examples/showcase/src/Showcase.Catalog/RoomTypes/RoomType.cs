namespace Showcase.Catalog.Entities;

/// <summary>
/// A type of room within a hotel property (e.g. Single, Double, Suite).
/// Demonstrates: [Autocomplete] on Name for search-as-you-type, and a domain key spanning two
/// columns — a room type code is unique <i>within its property</i>, not globally, which is what
/// [LogicKey] on both PropertyId and Code says.
/// </summary>
/// <remarks>
///     <para>
///         The scaffolding here is deliberately partial: create and update are written by hand, as
///         <c>CreateRoomTypeMutation</c> and <c>UpdateRoomTypeMutation</c> on <c>/api/room-types</c>,
///         and <c>[Resource]</c> supplies only what was missing. It is also the only soft-deletable
///         entity in the Showcase that is a scaffolded resource, which is what makes the soft
///         Delete/Restore pair reachable over HTTP rather than covered at the model level alone.
///     </para>
///     <para>
///         Read is in the set because the delete and restore answer with the read shape, and that
///         shape is only generated when Read is among the capabilities.
///     </para>
/// </remarks>
[Entity]
[Auditable]
[SoftDelete]
[Resource("room-types",
    Capabilities = ResourceCapabilities.Read | ResourceCapabilities.Delete | ResourceCapabilities.Restore)]
[Relation.ManyToMany<Amenity, RoomTypeAmenity>.WithNavigation("Amenities")]
[Relation.ManyToOne<Property>]
[Unique("PropertyId", "Code")]
public partial class RoomType : IEntity
{
    [Required]
    [Autocomplete]
    public string Name { get; private set; } = "";

    /// <summary>
    ///     The code the hotel uses for this room type. Unique within the property, not globally — which
    ///     is what <c>[Unique("PropertyId", "Code")]</c> on the class says: the key spans the generated
    ///     foreign key and this column.
    /// </summary>
    [Required]
    public string Code { get; private set; } = "";

    public string Description { get; private set; } = "";

    [Range(1, 100)]
    public int MaxOccupancy { get; private set; } = 2;

    /// <summary>Base rate per night. Cascade source for LineItem.UnitPrice.</summary>
    [Positive]
    [CascadeSource]
    public decimal BaseRate { get; private set; }

    /// <summary>ISO 4217 currency code.</summary>
    public string Currency { get; private set; } = "EUR";

    [Range(1, 10000)]
    public int TotalRooms { get; private set; }

}
