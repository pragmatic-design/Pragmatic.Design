using System.Data.Common;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     Generic connection factory that creates connections via a delegate.
///     Registered by the MigrationsBuilder with provider-specific connection constructors.
/// </summary>
public sealed class GenericConnectionFactory(
    string providerName,
    Func<string, DbConnection> createConnection) : IConnectionFactory
{
    public string ProviderName => providerName;

    public async Task<DbConnection> CreateOpenConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var connection = createConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return connection;
    }
}
