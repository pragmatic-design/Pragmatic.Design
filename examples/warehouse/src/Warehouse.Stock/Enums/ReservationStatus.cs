using Pragmatic;
using Warehouse.Stock.Contracts.Events;

namespace Warehouse.Stock.Enums;

/// <summary>
///     Where a hold is: held until it expires, made permanent by a confirmed order, or given back.
/// </summary>
[FastEnum]
public enum ReservationStatus
{
    /// <summary>Promised to an order that has not been confirmed, until <c>ExpiresAt</c>.</summary>
    [InitialState]
    Held,

    /// <summary>The order was confirmed: the hold no longer expires, and waits for the picker.</summary>
    [TransitionFrom(ReservationStatus.Held)]
    Confirmed,

    /// <summary>Taken off the shelf: the hold became a movement, and there is nothing left to hold.</summary>
    [TransitionFrom(ReservationStatus.Confirmed)]
    Picked,

    /// <summary>The order was cancelled while this was held: the stock is available again.</summary>
    [TransitionFrom(ReservationStatus.Held)]
    [TransitionFrom(ReservationStatus.Confirmed)]
    Released,

    /// <summary>The order was cancelled after it was picked: the goods went back to their shelf.</summary>
    [TransitionFrom(ReservationStatus.Picked)]
    Returned,

    /// <summary>Nobody confirmed the order in time, and the stock is available again.</summary>
    [TransitionFrom(ReservationStatus.Held)]
    [RaisesEvent<ReservationExpired>]
    Expired
}
