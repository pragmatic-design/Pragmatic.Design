using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Database;
using Pragmatic.Composition.Enums;

namespace Invoicing.Host;

/// <summary>
///     The one database of the monolith: both modules write in it, so an invoice and the customer it was
///     issued for are read in one transaction — and one schema is migrated, not two.
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;
