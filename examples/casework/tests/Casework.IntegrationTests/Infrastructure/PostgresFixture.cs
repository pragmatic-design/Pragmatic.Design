using Npgsql;
using Testcontainers.PostgreSql;

namespace Casework.IntegrationTests.Infrastructure;

/// <summary>
///     The PostgreSQL every test shares, started once per run — and the two databases the two services
///     own on it.
/// </summary>
/// <remarks>
///     <para>
///         One server, two databases: each host creates its own schema at startup, the way it does on a
///         developer's machine. The server is shared because this is a test run and not a deployment;
///         neither service is told the other's database is next door, and nothing in either connection
///         string names it.
///     </para>
///     <para>
///         <see cref="CreateDatabaseAsync" /> makes the two services' databases. A tenant's own database
///         is not made here: the host's provisioner makes it, as it would in a deployment — see
///         <see cref="ConnectionStringTemplate" />.
///     </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("casework")
        .WithUsername("casework")
        .WithPassword("Casework@Test!")
        .Build();

    /// <summary>Intake's database: the one its host migrates and writes.</summary>
    public string IntakeConnectionString { get; private set; } = null!;

    /// <summary>Verify's database. A different one, which is the whole point of the example.</summary>
    public string VerifyConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        IntakeConnectionString = await CreateDatabaseAsync("intake");
        VerifyConnectionString = await CreateDatabaseAsync("verify");
    }

    /// <summary>
    ///     The template a host interpolates a tenant id into, for a database on this same server.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The databases it names do <b>not</b> exist, and nothing creates them on first access, so
    ///     whoever wants one creates and migrates it explicitly — which is what
    ///     <c>ADatabasePerOrganisation</c> asserts and onboarding does over the bus.
    ///     The template's <c>{0}</c> is the tenant id, and <c>TenantDatabaseOptions.BuildConnectionString</c>
    ///     refuses an id with anything but letters, digits, <c>-</c> and <c>_</c> before formatting it —
    ///     a connection string is not a place to interpolate whatever arrived in a claim.
    /// </remarks>
    public string ConnectionStringTemplate => Of("casework_tenant_{0}");

    /// <summary>A connection string for one tenant's dedicated database in Intake, from the template.</summary>
    public string ConnectionStringFor(string tenantId) => Of($"casework_tenant_{tenantId}");

    /// <summary>
    ///     The same, for <b>Verify</b> — a different database name.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Separate on purpose: an organisation's verifications are not its cases, the two services
    ///     provision their own, and a test that pointed both at one database would pass while proving the
    ///     opposite of that separation.
    /// </remarks>
    public string VerifyConnectionStringTemplate => Of("casework_verify_{0}");

    public string VerifyConnectionStringFor(string tenantId) => Of($"casework_verify_{tenantId}");

    /// <summary>A new, empty database on the same server.</summary>
    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        var name = $"casework_{prefix}_{Guid.NewGuid():N}";
        var created = await _container.ExecScriptAsync($"CREATE DATABASE {name};");
        if (created.ExitCode != 0)
            throw new InvalidOperationException($"CREATE DATABASE {name} failed: {created.Stderr}");

        return Of(name);
    }

    /// <summary>
    ///     This server's connection string, pointed at one database and asking for error detail.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>Include Error Detail</c> is on for the whole suite: without it PostgreSQL's most useful
    ///     failures arrive as "DETAIL: Detail redacted as it may contain sensitive data", and a duplicate
    ///     key says neither which key nor which value. It is a test server with test rows in it; a
    ///     deployment leaves it off.
    /// </remarks>
    private string Of(string database)
        => new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database,
            IncludeErrorDetail = true
        }.ConnectionString;

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
