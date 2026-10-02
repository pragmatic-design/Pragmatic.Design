using System.Data.Common;
using Npgsql;
using Pragmatic.Configuration.Database;
using Testcontainers.PostgreSql;
using Xunit;

namespace Pragmatic.Configuration.Database.Tests.Postgres;

/// <summary>PostgreSQL container for the native LISTEN/NOTIFY watch suite. Skips gracefully if Docker is down.</summary>
public sealed class PostgresConfigurationFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>Connection string, or <c>null</c> when Docker is unavailable (tests skip).</summary>
    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder().Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch
        {
            ConnectionString = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>An <see cref="IDbConnectionFactory" /> producing Npgsql connections for the container.</summary>
    public sealed class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory
    {
        public DbConnection CreateConnection() => new NpgsqlConnection(connectionString);
    }
}
