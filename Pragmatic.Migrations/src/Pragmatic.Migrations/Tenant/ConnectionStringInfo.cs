using System.Data.Common;

namespace Pragmatic.Migrations.Tenant;

/// <summary>
///     Reads from a connection string the one thing several places need to name: its database.
/// </summary>
public static class ConnectionStringInfo
{
    /// <summary>The database a connection string names, however the provider spells it.</summary>
    /// <remarks>
    ///     ⚠️ <see cref="DbConnectionStringBuilder" /> and not a provider's builder: the callers serve
    ///     every provider, and the spellings that matter are <c>Database</c> (PostgreSQL, MySQL),
    ///     <c>Initial Catalog</c> (SQL Server) and <c>Data Source</c> (SQLite). A connection string this
    ///     cannot parse still produces a name, because a database nobody can name is exactly the one
    ///     worth showing — in a migration report or in the sentence a failed tenant connection carries.
    /// </remarks>
    public static string DatabaseIn(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "(none)";

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

            foreach (var key in new[] { "Database", "Initial Catalog", "Data Source" })
                if (builder.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } name)
                    return name;

            return "(unnamed)";
        }
        catch (ArgumentException)
        {
            return "(unparseable)";
        }
    }
}
