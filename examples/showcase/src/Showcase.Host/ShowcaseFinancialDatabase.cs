
namespace Showcase.Host;

/// <summary>
/// Financial database — owns the Billing boundary only.
/// Isolated from operational data for compliance and audit reasons.
/// Billing receives Booking data via domain events (ReservationConfirmed),
/// NOT via direct SQL join — cross-database ReadAccess is not permitted.
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Financial")]
public sealed class ShowcaseFinancialDatabase : PragmaticDatabase;
