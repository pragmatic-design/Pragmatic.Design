namespace Pragmatic.Aot.Host;

/// <summary>The smoke's database: a SQLite file, migrated to the Notes boundary's schema at start.</summary>
[PragmaticDatabase(Provider = DatabaseProvider.SQLite, ConfigKey = "ConnectionStrings:Notes")]
public sealed class NotesDatabase : PragmaticDatabase;
