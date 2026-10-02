using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.ConnectionString;

/// <summary>
///     Static connection string provider that reads from configuration.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type.</typeparam>
public sealed class ConfigurationConnectionStringProvider<TDbContext> : IConnectionStringProvider<TDbContext>
    where TDbContext : DbContext
{
    private readonly string _connectionString;

    /// <summary>
    ///     Creates a new instance with the specified connection string.
    /// </summary>
    public ConfigurationConnectionStringProvider(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <inheritdoc />
    public ValueTask<string> GetConnectionStringAsync(CancellationToken ct = default)
    {
        return new ValueTask<string>(_connectionString);
    }
}