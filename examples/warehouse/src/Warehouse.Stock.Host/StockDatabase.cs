using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Enums;
using Pragmatic.Composition.Database;

namespace Warehouse.Stock.Host;

/// <summary>
///     Stock's own database, shared by every instance of this host: two processes, one set of levels.
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Stock")]
public sealed class StockDatabase : PragmaticDatabase;
