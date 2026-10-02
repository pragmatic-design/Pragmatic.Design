using Pragmatic.Messaging.Attributes;

namespace Warehouse.Shipping;

/// <summary>
///     Shipping: what is packed for an order, and when it went out.
/// </summary>
/// <remarks>
///     <c>[EnableOutbox]</c>: a dispatch tells the others so, and the message is written in the
///     transaction that dispatches.
/// </remarks>
[Boundary]
[EnableOutbox]
public partial class ShippingBoundary;
