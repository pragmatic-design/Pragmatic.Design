// ReSharper disable once CheckNamespace
namespace Pragmatic.Aot.Host;

/// <summary>
///     The smoke's database, as the topology names it: a MongoDB database, whose connection string names it in its path. The provider is
///     the declaration's, as on the Showcase's documents host: no SQL driver is referenced, so Pragmatic Migrations applies the boundary's
///     documents schema and migrates no SQL (#555).
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Notes")]
public sealed class NotesDatabase : PragmaticDatabase;
