
namespace Showcase.Billing.Host;

/// <summary>
///     Financial database for standalone Billing host.
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Financial")]
public sealed class BillingFinancialDatabase : PragmaticDatabase;
