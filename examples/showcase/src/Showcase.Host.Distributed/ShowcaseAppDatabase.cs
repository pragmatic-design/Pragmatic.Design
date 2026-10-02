
namespace Showcase.Host.Distributed;

/// <summary>
///     Operational database — Accounts + Booking + Catalog.
///     Billing is NOT here — it runs on a separate host.
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class ShowcaseAppDatabase : PragmaticDatabase;
