using Pragmatic;
using Warehouse.Orders.Contracts.Events;

namespace Warehouse.Orders.Enums;

/// <summary>
///     Where an order has got to: drafted, placed, its stock reserved, picked off the shelves, shipped — or
///     cancelled on the way.
/// </summary>
/// <remarks>
///     Every move that exists is declared here and the generated <c>TransitionTo</c> refuses any other, so
///     no operation carries an <c>if</c> about the status. The moves after <c>Placed</c> are made by the
///     other services' answers: the reservation (<c>PlaceOrderAction</c>) and the picking and dispatch
///     (<c>OrderFulfilmentSaga</c>).
/// </remarks>
[FastEnum]
public enum OrderStatus
{
    /// <summary>Written and not yet sent: the customer can still change their mind for free.</summary>
    [InitialState]
    Draft,

    /// <summary>Sent. Only ever passed through: placing moves on to Reserved in the same request, or back to nothing.</summary>
    [TransitionFrom(OrderStatus.Draft)]
    Placed,

    /// <summary>The stock for every line is held for this order.</summary>
    [TransitionFrom(OrderStatus.Placed)]
    Reserved,

    /// <summary>The customer confirmed: Stock makes the holds permanent, and picking can start.</summary>
    [TransitionFrom(OrderStatus.Reserved)]
    [RaisesEvent<OrderReadyToPick>]
    ReadyToPick,

    [TransitionFrom(OrderStatus.ReadyToPick)]
    Picked,

    [TransitionFrom(OrderStatus.Picked)]
    Shipped,

    /// <summary>Stopped before it left. From anywhere up to the shelf, and never after.</summary>
    /// <remarks>
    ///     Not from <c>Shipped</c>: the goods are in a van, and what undoes that is a return, which is a
    ///     process of its own and not a status of this one.
    ///     <para>
    ///         <c>OrderCancelled</c> is raised by the move itself, whatever made it — a person, or an expired
    ///         hold — and each of the other services undoes its own part from it.
    ///     </para>
    /// </remarks>
    [TransitionFrom(OrderStatus.Draft)]
    [TransitionFrom(OrderStatus.Placed)]
    [TransitionFrom(OrderStatus.Reserved)]
    [TransitionFrom(OrderStatus.ReadyToPick)]
    [TransitionFrom(OrderStatus.Picked)]
    [RaisesEvent<OrderCancelled>]
    Cancelled
}
