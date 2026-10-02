using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Pragmatic.Messaging.Tests.Sql;

/// <summary>SQL Server container for the SQL transport suite (polling-only path).</summary>
public sealed class SqlTransportMsSqlFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public string? ConnectionString { get; private set; }

    public void Configure(DbContextOptionsBuilder db) => db.UseSqlServer(ConnectionString!);

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder().Build();
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
