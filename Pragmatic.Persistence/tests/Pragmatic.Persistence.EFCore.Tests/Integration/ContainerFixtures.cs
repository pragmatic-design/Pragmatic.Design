using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     One PostgreSQL container for a test class, instead of one per test.
/// </summary>
/// <remarks>
///     The container was a field on the test class with <c>IAsyncLifetime</c>, and xUnit builds a new
///     instance of a test class for every test method — so the container was started and torn down
///     once per test. ⚠️ Only the container is shared: each test still gets a database of its own,
///     because these tests count rows and a neighbour's rows would be in the count.
/// </remarks>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}

/// <summary>
///     One SQL Server container for a test class. See <see cref="PostgresContainerFixture" /> for why.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
