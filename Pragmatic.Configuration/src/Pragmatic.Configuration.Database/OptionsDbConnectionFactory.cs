using System.Data.Common;
using Microsoft.Extensions.Options;

namespace Pragmatic.Configuration.Database;

/// <summary>
///     Opens <see cref="DatabaseConfigurationOptions.ConnectionString" /> with
///     <see cref="DatabaseConfigurationOptions.ProviderFactory" />: the connection the options describe, for an
///     application that registered no <see cref="IDbConnectionFactory" /> of its own.
/// </summary>
internal sealed class OptionsDbConnectionFactory : IDbConnectionFactory
{
    private readonly DbProviderFactory _provider;
    private readonly string _connectionString;

    public OptionsDbConnectionFactory(IOptions<DatabaseConfigurationOptions> options)
    {
        var value = options.Value;

        // Refused at resolution, not at the first query: there an empty connection string would surface as
        // a provider error on the first read, far from the registration that left it out.
        if (value.ProviderFactory is null || string.IsNullOrWhiteSpace(value.ConnectionString))
        {
            throw new InvalidOperationException(
                "The database configuration store has no way to connect. Set both ConnectionString and "
                + "ProviderFactory (e.g. NpgsqlFactory.Instance) in AddDatabaseConfigurationStore, or register "
                + "an IDbConnectionFactory.");
        }

        _provider = value.ProviderFactory;
        _connectionString = value.ConnectionString;
    }

    public DbConnection CreateConnection()
    {
        var connection = _provider.CreateConnection()
            ?? throw new InvalidOperationException($"{_provider.GetType().Name} created no connection.");
        connection.ConnectionString = _connectionString;
        return connection;
    }
}
