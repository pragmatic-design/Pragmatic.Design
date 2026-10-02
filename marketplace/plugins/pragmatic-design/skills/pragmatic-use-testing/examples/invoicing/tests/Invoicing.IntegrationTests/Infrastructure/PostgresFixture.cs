using Npgsql;
using Testcontainers.PostgreSql;

namespace Invoicing.IntegrationTests.Infrastructure;

/// <summary>
///     The PostgreSQL every test shares, started once per run. The host creates its schema at startup,
///     the way it does on a developer's machine.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("invoicing")
        .WithUsername("invoicing")
        .WithPassword("Invoicing@Test!")
        .Build();

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    /// <summary>
    ///     A new, empty database on the same server, for a class whose reads are about <b>everything</b>
    ///     there is.
    /// </summary>
    /// <remarks>
    ///     The nightly sweep fans out over every active company, so on the shared database it also chases
    ///     the companies the other classes left behind: the assertions here name their own recipients and
    ///     hold either way, but the work grows with the suite and a count would not. One server, a database
    ///     per class that needs it — the host creates its schema at startup, as on the shared one.
    /// </remarks>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"invoicing_{Guid.NewGuid():N}";
        var created = await _container.ExecScriptAsync($"CREATE DATABASE {name};");
        if (created.ExitCode != 0)
            throw new InvalidOperationException($"CREATE DATABASE {name} failed: {created.Stderr}");

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
