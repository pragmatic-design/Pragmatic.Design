namespace Warehouse.Orders.Mutations;

/// <summary>
///     The order is stopped before it leaves.
/// </summary>
/// <remarks>
///     No body and no <c>if</c> about the status: which states may be cancelled is written once, on
///     <see cref="OrderStatus.Cancelled" />, and the invoker answers 409 from any other.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(OrdersPermissions.Order.Cancel)]
[Endpoint(HttpVerb.Post, "api/orders/{id}/cancel")]
[TransitionsTo<OrderStatus>(OrderStatus.Cancelled)]
[ReturnsDto<OrderDto>]
public partial class CancelOrderMutation : Mutation<Order, ConflictError>
{
    public required Guid Id { get; init; }
}
