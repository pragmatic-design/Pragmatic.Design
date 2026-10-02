using System.Data.Common;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Provides seed data to be executed after a successful migration.
///     Register in DI to automatically seed after each migration run.
/// </summary>
public interface IMigrationSeedProvider
{
    /// <summary>
    ///     Database name filter. Return null to seed all databases.
    /// </summary>
    string? DatabaseName { get; }

    /// <summary>
    ///     Seed priority (lower = first). Default: 0.
    /// </summary>
    int Order => 0;

    /// <summary>
    ///     Executes seed data SQL/logic on the given connection.
    ///     Called only when the migration applied at least 1 change (not on no-op runs).
    /// </summary>
    Task SeedAsync(DbConnection connection, CancellationToken ct = default);
}
