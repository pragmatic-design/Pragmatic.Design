namespace Warehouse.Orders.Mutations;

/// <summary>
///     Stock picked the order: <c>ReadyToPick → Picked</c>. Asked by the fulfilment process; no endpoint.
/// </summary>
/// <remarks>
///     <c>IsConditional</c>: only a <c>ReadyToPick</c> order moves, and any other state is left alone. That
///     is what makes a repeated message move the order once — the second finds it <c>Picked</c> and changes
///     nothing — without the handler having to tell a repeat from a refusal.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(OrdersPermissions.Order.Update)]
[TransitionsTo<OrderStatus>(OrderStatus.Picked, When = TransitionTiming.ByBody, IsConditional = true)]
public partial class MarkOrderPickedMutation : Mutation<Order, ConflictError>
{
    public required Guid Id { get; init; }

    public override Task<Result<Order, IError>> ApplyAsync(Order entity, CancellationToken ct = default)
    {
        if (entity.Status != OrderStatus.ReadyToPick)
            return Task.FromResult(Result<Order, IError>.Success(entity));

        var moved = entity.TransitionTo(OrderStatus.Picked);
        return Task.FromResult(moved.IsFailure
            ? Result<Order, IError>.Failure(moved.Error)
            : Result<Order, IError>.Success(entity));
    }
}
