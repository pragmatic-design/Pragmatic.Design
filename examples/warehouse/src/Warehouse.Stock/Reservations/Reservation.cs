namespace Warehouse.Stock.Entities;

/// <summary>
///     Part of a level held for an order: which product, where, how many, and until when.
/// </summary>
/// <remarks>
///     <para>
///         Written by <see cref="StockLevel.Hold" />, in the transaction that raises the level's
///         <c>Reserved</c>, and given back by <see cref="Expire" /> in the one that lowers it: the two
///         always say the same thing, and one without the other would be a promise nobody could find, or
///         one nobody made.
///     </para>
///     <para>
///         The product is a key and not a SKU, because inside Stock a product is its row; the SKU is how
///         the other services name it, and it arrives on the request.
///     </para>
/// </remarks>
[Entity]
[StateMachine<ReservationStatus>]
[Relation.ManyToOne<Product>]
[Relation.ManyToOne<Location>]
public partial class Reservation : DomainEventSource, IEntity
{
    /// <summary>The order this is held for: Orders' id, a key across the services, never a navigation.</summary>
    public Guid OrderId { get; private set; }

    [GreaterThan(0)]
    public int Quantity { get; private set; }

    /// <summary>When the hold is given back if the order has not been confirmed by then.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Moved only through the machine: held, then confirmed or expired, and never back.</summary>
    public ReservationStatus Status { get; private set; } = ReservationStatus.Held;

    internal static Reservation Of(StockLevel level, Guid orderId, int quantity, DateTimeOffset expiresAt)
    {
        var reservation = Create();
        reservation.SetProductId(level.ProductId);
        reservation.SetLocationId(level.LocationId);
        reservation.OrderId = orderId;
        reservation.Quantity = quantity;
        reservation.ExpiresAt = expiresAt;
        return reservation;
    }

    /// <summary>
    ///     Gives the stock back to <paramref name="level" />: the machine refuses a hold that is not held,
    ///     so a confirmed one is never given back.
    /// </summary>
    internal VoidResult<IError> Expire(StockLevel level)
    {
        var moved = TransitionTo(ReservationStatus.Expired);
        if (moved.IsFailure)
            return moved;

        level.Release(Quantity);
        return VoidResult<IError>.Success();
    }
}
