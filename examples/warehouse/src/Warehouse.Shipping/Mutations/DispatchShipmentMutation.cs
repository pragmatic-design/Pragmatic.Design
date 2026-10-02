namespace Warehouse.Shipping.Mutations;

/// <summary>
///     A shipping clerk hands a shipment to its carrier: <c>Created → Dispatched</c>, once.
/// </summary>
/// <remarks>
///     No body: the move is the invoker's, a second dispatch is refused by the machine with 409, and
///     <c>ShipmentDispatched</c> is raised by the machine and published through the outbox in the same
///     transaction — carrying the tracking number, which the customer follows the parcel by.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(ShippingPermissions.Shipment.Dispatch)]
[Endpoint(HttpVerb.Post, "api/shipments/{id}/dispatch")]
[TransitionsTo<ShipmentStatus>(ShipmentStatus.Dispatched)]
[ReturnsDto<ShipmentDto>]
public partial class DispatchShipmentMutation : Mutation<Shipment, ConflictError>
{
    public required Guid Id { get; init; }

    [Required]
    [MaxLength(60)]
    public required string Carrier { get; init; }

    [GreaterThan(0)]
    public required int Packages { get; init; }
}
