using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
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

/// <summary>
///     One MySQL 8.4 container for the whole scenario class, started with Testcontainers' generic builder. See
///     <see cref="SqlServerContainerFixture" /> for why, and for what is deliberately <em>not</em> shared.
/// </summary>
public sealed class MySqlContainerFixture : IAsyncLifetime
{
    public const string Password = "scenario";

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("mysql:8.4")
        .WithEnvironment("MYSQL_ROOT_PASSWORD", Password)
        .WithPortBinding(3306, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("ready for connections.*port: 3306"))
        .Build();

    /// <summary>The server as root, without a database: each test creates its own.</summary>
    public string ConnectionString => $"Server={_container.Hostname};Port={_container.GetMappedPublicPort(3306)};User ID=root;Password={Password}";

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}

/// <summary>
///     One Oracle 23ai Free container for the whole scenario class. See <see cref="SqlServerContainerFixture" /> for why; on
///     Oracle a database of its own is a user of its own, created by SYSTEM.
/// </summary>
public sealed class OracleContainerFixture : IAsyncLifetime
{
    public const string Password = "scenario";

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("gvenzl/oracle-free:23-faststart")
        .WithEnvironment("ORACLE_PASSWORD", Password)
        .WithPortBinding(1521, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("DATABASE IS READY TO USE!"))
        .Build();

    /// <summary>A connection string for <paramref name="user" /> on the pluggable database.</summary>
    public string ConnectionString(string user) =>
        $"User Id={user};Password={Password};Data Source={_container.Hostname}:{_container.GetMappedPublicPort(1521)}/FREEPDB1";

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
