namespace Pragmatic.Identity.Persistence.Configuration;

/// <summary>
///     Builds the provider-specific <c>WHERE ValidTo IS NULL</c> filter SQL used for
///     partial (filtered) unique indexes that enforce "only one ACTIVE row" on temporal
///     membership/permission tables.
/// </summary>
/// <remarks>
///     <para>
///         The persisted column name is the default <c>ValidTo</c> (no renaming is configured),
///         so only the identifier-quoting differs per provider:
///         SQL Server uses <c>[ValidTo]</c>, PostgreSQL / SQLite use <c>"ValidTo"</c>.
///     </para>
///     <para>
///         Detection is by <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.ProviderName"/>
///         string rather than the provider-specific <c>IsNpgsql()/IsSqlite()</c> extension methods,
///         so this package takes no dependency on any concrete provider package.
///     </para>
///     <para>
///         Providers that cannot express a partial index (notably EF Core InMemory, which ignores
///         indexes entirely) return <see langword="null"/>; callers then fall back to a non-filtered
///         index for that provider.
///     </para>
/// </remarks>
internal static class TemporalIndexFilter
{
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string InMemoryProvider = "Microsoft.EntityFrameworkCore.InMemory";

    /// <summary>
    ///     Returns the <c>WHERE ValidTo IS NULL</c> filter SQL for the given EF Core provider name,
    ///     or <see langword="null"/> when the provider cannot express a partial index (callers then
    ///     fall back to a plain, non-unique lookup index).
    /// </summary>
    /// <param name="providerName">
    ///     The value of <c>Database.ProviderName</c> (e.g. <c>"Microsoft.EntityFrameworkCore.Sqlite"</c>),
    ///     or <see langword="null"/> when no provider is configured yet.
    /// </param>
    public static string? ActiveRowFilter(string? providerName) => providerName switch
    {
        SqlServerProvider => "[ValidTo] IS NULL",
        SqliteProvider => "\"ValidTo\" IS NULL",
        NpgsqlProvider => "\"ValidTo\" IS NULL",
        // InMemory ignores indexes entirely and is non-relational: no filter (no enforcement).
        InMemoryProvider => null,
        null => null,
        // Unknown relational provider: assume standard SQL identifier quoting.
        _ => "\"ValidTo\" IS NULL"
    };
}
