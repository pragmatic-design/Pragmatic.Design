using Testcontainers.PostgreSql;

namespace Conformance.Tests.Infrastructure;

/// <summary>
///     The PostgreSQL container shared by all the E2E cases.
/// </summary>
/// <remarks>
///     Started once per run. <see cref="ConformanceSchema" /> applies the schema.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("conformance")
        .WithUsername("pragmatic")
        .WithPassword("Pragmatic@Test!")
        .Build();

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        ConnectionString = _container.GetConnectionString();
        await ConformanceSchema.ApplyAsync(ConnectionString).ConfigureAwait(false);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
