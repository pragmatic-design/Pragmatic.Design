using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Enums;
using Pragmatic.Composition.Database;

namespace Warehouse.Shipping.Host;

/// <summary>
///     Shipping's own database. A shipment knows its order by id and nothing else of the other services.
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Shipping")]
public sealed class ShippingDatabase : PragmaticDatabase;
