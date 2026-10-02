using Npgsql;
using Testcontainers.PostgreSql;

namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     The PostgreSQL every test shares, started once per run. The host creates its schema at startup,
///     the way it does on a developer's machine.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("timeoff")
        .WithUsername("timeoff")
        .WithPassword("TimeOff@Test!")
        .Build();

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    /// <summary>
    ///     A new, empty database on the same server, for a test that changes what the others share — sealing
    ///     the audit trail's current hour refuses every later write to it, whichever class makes it.
    /// </summary>
    /// <returns>Its connection string. The host run on it creates its schema at startup, as on the shared one.</returns>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"timeoff_{Guid.NewGuid():N}";
        var created = await _container.ExecScriptAsync($"CREATE DATABASE {name};");
        if (created.ExitCode != 0)
            throw new InvalidOperationException($"CREATE DATABASE {name} failed: {created.Stderr}");

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
