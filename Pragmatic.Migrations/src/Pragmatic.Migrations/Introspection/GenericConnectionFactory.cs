using System.Data.Common;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     Generic connection factory that creates connections via a delegate.
///     Registered by the MigrationsBuilder with provider-specific connection constructors.
/// </summary>
/// <param name="providerName">The provider the connections are to.</param>
/// <param name="createConnection">Creates an unopened connection for a connection string.</param>
/// <param name="sessionSetup">
///     A statement run on each connection once it is open, or null. MySQL's: the statements this library writes quote
///     identifiers with double quotes, which MySQL reads as identifiers only under <c>ANSI_QUOTES</c>.
/// </param>
public sealed class GenericConnectionFactory(
    string providerName,
    Func<string, DbConnection> createConnection,
    string? sessionSetup = null) : IConnectionFactory
{
    public string ProviderName => providerName;

    public async Task<DbConnection> CreateOpenConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var connection = createConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            if (sessionSetup is not null)
            {
                var setup = connection.CreateCommand();
                await using (setup.ConfigureAwait(false))
                {
                    setup.CommandText = sessionSetup;
                    await setup.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return connection;
    }
}
