using Npgsql;
using Testcontainers.PostgreSql;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     One PostgreSQL server and the three databases the services own on it.
/// </summary>
/// <remarks>
///     One server because this is a test run and not a deployment; no service is told the others'
///     databases are next door, and nothing in its connection string names them. The two Stock instances
///     share Stock's database, which is what makes them one service rather than two.
/// </remarks>
internal sealed class PostgresFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("warehouse")
        .WithUsername("warehouse")
        .WithPassword("Warehouse@Test!")
        .Build();

    public string OrdersConnectionString { get; private set; } = null!;

    public string StockConnectionString { get; private set; } = null!;

    public string ShippingConnectionString { get; private set; } = null!;

    public async Task StartAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);

        OrdersConnectionString = await CreateDatabaseAsync("orders").ConfigureAwait(false);
        StockConnectionString = await CreateDatabaseAsync("stock").ConfigureAwait(false);
        ShippingConnectionString = await CreateDatabaseAsync("shipping").ConfigureAwait(false);
    }

    private async Task<string> CreateDatabaseAsync(string service)
    {
        var name = $"warehouse_{service}";
        var created = await _container.ExecScriptAsync($"CREATE DATABASE {name};").ConfigureAwait(false);
        if (created.ExitCode != 0)
            throw new InvalidOperationException($"CREATE DATABASE {name} failed: {created.Stderr}");

        // Error detail on: without it PostgreSQL's most useful failures arrive redacted.
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
            IncludeErrorDetail = true,
        }.ConnectionString;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
