namespace Warehouse.Orders.Entities;

/// <summary>
///     What a customer asked for — its lines — and where it has got to.
/// </summary>
/// <remarks>
///     <para>
///         The lines name products by SKU and nothing else: the catalogue is Stock's, and a copy of it
///         here would be a second answer to what a product is. Whether the SKU exists and is on the shelf
///         is Stock's question, asked when the order is placed.
///     </para>
///     <para>
///         The lines cascade: a line has no life without its order, and nothing addresses one on its own.
///     </para>
/// </remarks>
[Entity]
[StateMachine<OrderStatus>]
[Relation.OneToMany<OrderLine>.WithNavigation("Lines", Inverse = "Order", OnDelete = DeleteBehavior.Cascade)]
public partial class Order : DomainEventSource, IEntity
{
    /// <summary>What the customer quotes: <c>ORD-000001</c>, drawn from a database sequence by the save.</summary>
    [LogicKey]
    [GeneratedValue("ORD-{SEQ:6}")]
    public string Number { get; private set; } = "";

    /// <summary>Who the order is for, as the shop that sent it knows them.</summary>
    [Required]
    [MaxLength(64)]
    public string CustomerReference { get; private set; } = "";

    /// <summary>Moved only through the machine, which refuses every move nobody declared.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;

    /// <summary>Why the order was cancelled when something other than a person cancelled it; null otherwise.</summary>
    [MaxLength(200)]
    public string? CancellationReason { get; private set; }

    /// <summary>
    ///     Cancels the order and says why. The machine decides whether it may be cancelled at all.
    /// </summary>
    internal VoidResult<IError> CancelBecause(string reason)
    {
        var moved = TransitionTo(OrderStatus.Cancelled);
        if (moved.IsFailure)
            return moved;

        CancellationReason = reason;
        return VoidResult<IError>.Success();
    }
}
