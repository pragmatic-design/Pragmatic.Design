namespace Pragmatic.Persistence.Sql.Core.Tests.ValueObjects;

/// <summary>
///     The Shipments table with its value objects behind private setters, as a Pragmatic entity keeps them (<c>Amenity.Support</c> in the
///     Showcase): the reader cannot assign them in an initializer, and sets them through their accessor.
/// </summary>
[Entity]
[Table("Shipments")]
public sealed class GuardedParcel
{
    public Guid Id { get; set; }

    [Convert<ShipmentCodeConverter>]
    public ShipmentCode Code { get; set; } = ShipmentCode.Create("");

    public ShipmentAddress Destination { get; private set; } = new("", "", 0);

    public ShipmentAddress? Return { get; private set; }

    /// <summary>The only way in: the addresses are the parcel's to set.</summary>
    public void Ship(ShipmentAddress destination, ShipmentAddress? returnTo)
    {
        Destination = destination;
        Return = returnTo;
    }
}
