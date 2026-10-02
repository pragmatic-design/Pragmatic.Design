using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Messaging.Tests.Sql;

/// <summary>SQL transport suite on PostgreSQL (Testcontainers) — includes the pg_notify path.</summary>
[Collection("SqlTransportPostgres")]
public sealed class PostgresSqlTransportTests(SqlTransportPostgresFixture fixture) : SqlTransportIntegrationSuite
{
    protected override string? ConnectionString => fixture.ConnectionString;

    protected override void Configure(DbContextOptionsBuilder db) => fixture.Configure(db);
}
