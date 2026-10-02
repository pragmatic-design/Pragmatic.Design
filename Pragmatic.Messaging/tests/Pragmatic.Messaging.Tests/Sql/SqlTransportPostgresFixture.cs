using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Pragmatic.Messaging.Tests.Sql;

/// <summary>PostgreSQL container for the SQL transport suite (pg_notify path included).</summary>
public sealed class SqlTransportPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string? ConnectionString { get; private set; }

    public void Configure(DbContextOptionsBuilder db) => db.UseNpgsql(ConnectionString!);

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
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
            ConnectionString = null; // Docker unavailable — tests skip gracefully
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
#pragma warning restore CA2007
}
