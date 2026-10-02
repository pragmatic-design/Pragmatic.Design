namespace Warehouse.Stock.Entities;

/// <summary>
///     How much of one product one location holds, and how much of it is already promised.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>A level changes only through a movement.</b> <see cref="Receive" /> and
///         <see cref="Adjust" /> are the only writers of <see cref="OnHand" />, and each returns the
///         <see cref="StockMovement" /> that says why — which the operation adds in the same transaction.
///         There is no endpoint that sets a number.
///     </para>
///     <para>
///         One level per product and location: the pair is the domain key, on the class because both
///         parts are the keys the relations generate.
///     </para>
///     <para>
///         <c>[ConcurrencyAware]</c>: both Stock instances write levels, and the parts of an import land on
///         the same ones at once. Two writers that read the same version do not both win — the second
///         save fails, instead of silently undoing the first one's receipt.
///     </para>
/// </remarks>
[Entity]
[ConcurrencyAware]
[Relation.ManyToOne<Product>]
[Relation.ManyToOne<Location>]
[LogicKey("ProductId", "LocationId")]
public partial class StockLevel : IEntity
{
    /// <summary>What is physically there.</summary>
    public int OnHand { get; private set; }

    /// <summary>What is there and already promised to an order.</summary>
    public int Reserved { get; private set; }

    /// <summary>What an order could still be promised here.</summary>
    internal int AvailableNow() => OnHand - Reserved;

    /// <summary>The level of a product at a location that has never held it.</summary>
    internal static StockLevel Empty(Guid productId, Guid locationId)
    {
        var level = Create();
        level.SetProductId(productId);
        level.SetLocationId(locationId);
        return level;
    }

    /// <summary>Goods arrived: the level rises by what arrived.</summary>
    internal StockMovement Receive(int quantity)
    {
        OnHand += quantity;
        return StockMovement.Of(this, MovementKind.Receipt, quantity, reason: null);
    }

    /// <summary>
    ///     Promises part of what is here to an order: nothing leaves the shelf, but no other order can be
    ///     promised it.
    /// </summary>
    /// <remarks>
    ///     Not a movement: <see cref="OnHand" /> does not change. The caller has already checked that
    ///     <paramref name="quantity" /> is available — across every line of the order, which is a question
    ///     one level cannot answer.
    /// </remarks>
    internal Reservation Hold(Guid orderId, int quantity, DateTimeOffset expiresAt)
    {
        Reserved += quantity;
        return Reservation.Of(this, orderId, quantity, expiresAt);
    }

    /// <summary>
    ///     A confirmed hold is taken off the shelf: what was promised leaves, and a pick says so.
    /// </summary>
    /// <remarks>
    ///     Both numbers go down by the same quantity — the stock was promised and now it is gone — so what
    ///     is available to other orders does not change.
    /// </remarks>
    internal StockMovement Pick(int quantity)
    {
        OnHand -= quantity;
        Reserved -= quantity;
        return StockMovement.Of(this, MovementKind.Pick, -quantity, reason: null);
    }

    /// <summary>
    ///     Picked goods of a cancelled order go back on the shelf they came from: a receipt, saying why.
    /// </summary>
    internal StockMovement Return(int quantity)
    {
        OnHand += quantity;
        return StockMovement.Of(this, MovementKind.Receipt, quantity, reason: "returned: order cancelled");
    }

    /// <summary>A hold was given back: what it promised is available again.</summary>
    internal void Release(int quantity) => Reserved -= quantity;

    /// <summary>
    ///     A count found a difference: the level moves by <paramref name="delta" />, unless that would
    ///     leave less on hand than nothing.
    /// </summary>
    internal Result<StockMovement, IError> Adjust(int delta, string reason)
    {
        if (OnHand + delta < 0)
            return new StockWouldGoNegativeError { OnHand = OnHand, Delta = delta };

        OnHand += delta;
        return StockMovement.Of(this, MovementKind.Adjustment, delta, reason);
    }
}
