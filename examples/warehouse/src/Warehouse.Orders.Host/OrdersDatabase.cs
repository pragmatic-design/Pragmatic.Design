using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Database;
using Pragmatic.Composition.Enums;

namespace Warehouse.Orders.Host;

/// <summary>
///     Orders' own database. The stock and the shipments are in the other services' databases, and an
///     order never shares a transaction with either.
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Orders")]
public sealed class OrdersDatabase : PragmaticDatabase;
