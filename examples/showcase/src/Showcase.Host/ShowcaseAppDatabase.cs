
namespace Showcase.Host;

/// <summary>
/// Operational database — owns Booking and Catalog boundaries.
/// Booking reads Catalog entities (Property, RoomType) via SQL join,
/// which is valid because both boundaries share this database.
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class ShowcaseAppDatabase : PragmaticDatabase;
