using Pragmatic.Jobs.Attributes;
using Pragmatic.Messaging.Attributes;

namespace Warehouse.Stock;

/// <summary>
///     Stock: what is on the shelves, what is promised to an order, and every movement between the two.
/// </summary>
/// <remarks>
///     <para>
///         <c>[EnableJobPersistence]</c>: the expiry of a hold is a job row in this database, written in
///         the transaction that holds the stock, so it outlives the instance that took the request.
///     </para>
///     <para>
///         <c>[EnableOutbox]</c>: a hold that expires says so to Orders, and the message is written in the
///         transaction that gives the stock back.
///     </para>
///     <para>
///         <c>[EnableBatchProgress]</c>: the progress of a supplier's import is a table in this database,
///         so either instance counts the part it applied and either answers how far the import got.
///     </para>
/// </remarks>
[Boundary]
[EnableOutbox]
[EnableJobPersistence]
[EnableBatchProgress]
public partial class StockBoundary;
