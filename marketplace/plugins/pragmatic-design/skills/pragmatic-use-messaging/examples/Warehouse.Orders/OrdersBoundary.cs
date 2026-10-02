using Pragmatic.Messaging.Attributes;

namespace Warehouse.Orders;

/// <summary>
///     Orders: what was asked for, what was reserved for it, and what has left the building.
/// </summary>
/// <remarks>
///     <para>
///         <c>[EnableOutbox]</c>: a confirmed order tells Stock so, and the message is written in the
///         transaction that confirms it.
///     </para>
///     <para>
///         <c>[EnableSagaPersistence]</c>: the fulfilment process of each order is rows in this database,
///         so a restart of Orders in the middle of it resumes it rather than forgetting it.
///     </para>
/// </remarks>
[Boundary]
[EnableOutbox]
[EnableSagaPersistence]
public partial class OrdersBoundary;
