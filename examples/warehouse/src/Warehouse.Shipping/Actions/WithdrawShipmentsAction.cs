namespace Warehouse.Shipping.Actions;

/// <summary>
///     Withdraws the shipments of a cancelled order that have not left; the answer is how many.
/// </summary>
/// <remarks>
///     Only <c>Created</c> shipments are withdrawn, so a repeat finds none and changes nothing, and one
///     already dispatched is left alone — its order could not have been cancelled anyway. Each withdrawal
///     raises <c>ShipmentWithdrawn</c> through the outbox. No <c>[Endpoint]</c>; run by
///     <c>WithdrawTheShipmentOfACancelledOrder</c>.
/// </remarks>
[DomainAction]
[RequirePermission(ShippingPermissions.Shipment.Update)]
public partial class WithdrawShipmentsAction : DomainAction<int>
{
    private IRepository<Shipment> _shipments = null!;

    public required Guid OrderId { get; init; }

    public override async Task<Result<int, IError>> Execute(CancellationToken ct = default)
    {
        var waiting = await _shipments
            .FindAsync(Spec<Shipment>.Where(s => s.OrderId == OrderId && s.Status == ShipmentStatus.Created), ct)
            .ConfigureAwait(false);

        foreach (var shipment in waiting)
        {
            var withdrawn = shipment.TransitionTo(ShipmentStatus.Withdrawn);
            if (withdrawn.IsFailure)
                return Result<int, IError>.Failure(withdrawn.Error);
        }

        return waiting.Count;
    }
}
