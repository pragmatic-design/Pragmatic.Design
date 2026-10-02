namespace Pragmatic.Composition.Enums;

/// <summary>
///     Supported database providers for <see cref="Attributes.PragmaticDatabaseAttribute" />.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>Microsoft SQL Server via UseSqlServer()</summary>
    SqlServer,

    /// <summary>PostgreSQL via UseNpgsql()</summary>
    PostgreSql,

    /// <summary>SQLite via UseSqlite()</summary>
    SQLite,

    /// <summary>
    ///     MySQL via UseMySQL(), Oracle's <c>MySql.EntityFrameworkCore</c> — the only MySQL provider on EF Core
    ///     10. The application references that package itself; it is GPL-2.0 with the Universal FOSS
    ///     exception. MariaDB through it is not verified.
    /// </summary>
    MySql,

    /// <summary>EF Core in-memory provider via UseInMemoryDatabase()</summary>
    InMemory
}
