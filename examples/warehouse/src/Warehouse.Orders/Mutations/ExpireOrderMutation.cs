namespace Warehouse.Orders.Mutations;

/// <summary>
///     Stock gave back an order's hold because nobody confirmed it: the order is cancelled, and says why.
/// </summary>
/// <remarks>
///     <para>
///         No <c>[Endpoint]</c>: it is run by <c>CancelTheOrderWhoseHoldExpired</c> when the expiry
///         arrives from Stock.
///     </para>
///     <para>
///         <c>IsConditional</c>, because only a <c>Reserved</c> order is cancelled by an expiry. Any other
///         state is left alone, and that is what makes the consumer idempotent without remembering the
///         event: the second expiry of an order — a second hold of the same order, or a redelivery — finds
///         it <c>Cancelled</c> and changes nothing. And an order confirmed just before its hold expired is
///         not cancelled by an expiry that arrives after the confirmation; the machine alone would allow
///         it, because a <c>ReadyToPick</c> order may still be cancelled by a person.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(OrdersPermissions.Order.Cancel)]
[TransitionsTo<OrderStatus>(OrderStatus.Cancelled, When = TransitionTiming.ByBody, IsConditional = true)]
public partial class ExpireOrderMutation : Mutation<Order, ConflictError>
{
    /// <summary>What the order says when an expired hold cancelled it.</summary>
    public const string Reason = "reservation expired";

    public required Guid Id { get; init; }

    public override Task<Result<Order, IError>> ApplyAsync(Order entity, CancellationToken ct = default)
    {
        if (entity.Status != OrderStatus.Reserved)
            return Task.FromResult(Result<Order, IError>.Success(entity));

        var cancelled = entity.CancelBecause(Reason);
        return Task.FromResult(cancelled.IsFailure
            ? Result<Order, IError>.Failure(cancelled.Error)
            : Result<Order, IError>.Success(entity));
    }
}
