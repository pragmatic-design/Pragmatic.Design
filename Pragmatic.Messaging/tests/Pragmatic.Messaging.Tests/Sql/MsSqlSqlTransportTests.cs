using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Messaging.Tests.Sql;

/// <summary>SQL transport suite on SQL Server (Testcontainers) — polling-only path.</summary>
[Collection("SqlTransportMsSql")]
public sealed class MsSqlSqlTransportTests(SqlTransportMsSqlFixture fixture) : SqlTransportIntegrationSuite
{
    protected override string? ConnectionString => fixture.ConnectionString;

    protected override void Configure(DbContextOptionsBuilder db) => fixture.Configure(db);
}
