namespace Warehouse.Orders.Mutations;

/// <summary>
///     The customer confirms a reserved order: <c>Reserved → ReadyToPick</c>, and Stock is told so its
///     holds stop expiring.
/// </summary>
/// <remarks>
///     No body: the move is the invoker's, and <c>OrderReadyToPick</c> is raised by the machine
///     (<c>[RaisesEvent]</c> on the state) and published through the outbox in the same transaction.
///     ⚠️ Stock hears it asynchronously. A confirmation saved just before the hold's time is up can reach
///     Stock after the hold was given back; Orders then hears the expiry, and the order — no longer
///     <c>Reserved</c> — is not cancelled by it (<see cref="ExpireOrderMutation" />). The order stays
///     confirmed with its stock given back, and picking is where that is answered: Stock picks only
///     confirmed holds, and an order with none is refused there.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(OrdersPermissions.Order.Confirm)]
[Endpoint(HttpVerb.Post, "api/orders/{id}/confirm")]
[TransitionsTo<OrderStatus>(OrderStatus.ReadyToPick)]
[ReturnsDto<OrderDto>]
public partial class ConfirmOrderMutation : Mutation<Order, ConflictError>
{
    public required Guid Id { get; init; }
}
