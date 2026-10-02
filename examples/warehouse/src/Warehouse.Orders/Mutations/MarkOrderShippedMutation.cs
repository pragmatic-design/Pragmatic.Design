namespace Warehouse.Orders.Mutations;

/// <summary>
///     The shipment left: <c>Picked → Shipped</c>. Asked by the fulfilment process; no endpoint.
/// </summary>
/// <remarks>
///     Conditional for the same reason as <see cref="MarkOrderPickedMutation" />: only a <c>Picked</c> order
///     moves, so a repeat changes nothing.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(OrdersPermissions.Order.Update)]
[TransitionsTo<OrderStatus>(OrderStatus.Shipped, When = TransitionTiming.ByBody, IsConditional = true)]
public partial class MarkOrderShippedMutation : Mutation<Order, ConflictError>
{
    public required Guid Id { get; init; }

    public override Task<Result<Order, IError>> ApplyAsync(Order entity, CancellationToken ct = default)
    {
        if (entity.Status != OrderStatus.Picked)
            return Task.FromResult(Result<Order, IError>.Success(entity));

        var moved = entity.TransitionTo(OrderStatus.Shipped);
        return Task.FromResult(moved.IsFailure
            ? Result<Order, IError>.Failure(moved.Error)
            : Result<Order, IError>.Success(entity));
    }
}
