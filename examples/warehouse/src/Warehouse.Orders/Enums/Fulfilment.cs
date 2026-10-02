namespace Warehouse.Orders.Enums;

/// <summary>
///     Where an order's fulfilment process is: waiting for the picker, waiting for the carrier, or done.
/// </summary>
/// <remarks>
///     The state of the <b>process</b>, not of the order: the order has its own machine
///     (<see cref="OrderStatus" />), and the process decides when to move it.
/// </remarks>
public enum Fulfilment
{
    /// <summary>Confirmed; Stock has not picked it yet.</summary>
    AwaitingPick,

    /// <summary>Picked; its shipment has not left yet.</summary>
    AwaitingDispatch,

    /// <summary>Gone with its carrier. The process is over.</summary>
    Shipped,

    /// <summary>Cancelled; waiting for the services that did something to say they undid it.</summary>
    Compensating,

    /// <summary>Cancelled, and every service that had done something has undone it. The process is over.</summary>
    Compensated
}
