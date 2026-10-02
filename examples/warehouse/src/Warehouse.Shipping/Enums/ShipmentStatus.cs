using Pragmatic;
using Warehouse.Shipping.Contracts.Events;

namespace Warehouse.Shipping.Enums;

/// <summary>Where a shipment is: packed and waiting for its carrier, or gone.</summary>
[FastEnum]
public enum ShipmentStatus
{
    /// <summary>Made from a picked order, and waiting for the carrier.</summary>
    [InitialState]
    Created,

    /// <summary>Handed to the carrier. Nothing moves it back: a parcel on a van is not recalled from here.</summary>
    [TransitionFrom(ShipmentStatus.Created)]
    [RaisesEvent<ShipmentDispatched>]
    Dispatched,

    /// <summary>Its order was cancelled before it left: it is not going anywhere.</summary>
    [TransitionFrom(ShipmentStatus.Created)]
    [RaisesEvent<ShipmentWithdrawn>]
    Withdrawn
}
