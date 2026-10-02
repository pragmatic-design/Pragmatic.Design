using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Pragmatic.Migrations.Tests.Provider;

/// <summary>
///     One SQL Server container for the whole scenario class, instead of one per test.
/// </summary>
/// <remarks>
///     <para>
///         A container held as a field on the test class with <c>IAsyncLifetime</c> is not enough:
///         xUnit builds a new instance of a test class for every test method, so twenty-one containers
///         would be started and torn down to run twenty-one tests. Measured that way, the class took
///         <b>2m 27s</b> for <b>8.7s</b> of test execution, 86% of its suite.
///     </para>
///     <para>
///         ⚠️ A shared container is not a shared database, and for these tests the difference is the
///         whole test: <c>SchemaScenarioTestBase</c> runs the real <c>MigrationRunner</c>, which diffs
///         the desired schema against the <b>actual</b> database. Scenarios sharing one database would
///         each see the previous ones' tables in that diff and the runner would propose dropping them.
///         So the container is shared and the database is not — see <c>TheDatabase_StartsEmpty</c>,
///         which fails on the whole class if that ever stops being true.
///     </para>
/// </remarks>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    /// <summary>The container's own connection string, pointing at its default database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}

/// <summary>
///     One PostgreSQL container for the whole scenario class. See <see cref="SqlServerContainerFixture" />
///     for why, and for what is deliberately <em>not</em> shared.
/// </summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    /// <summary>The container's own connection string, pointing at its default database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
