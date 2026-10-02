using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Sql;

/// <summary>
///     P4 — concurrent / online index creation. Each provider emits its own form when
///     <see cref="AddIndex.IsConcurrent" /> is true; the runner defers execution to a
///     post-commit phase outside the transaction (verified by SQLite end-to-end in
///     <c>DataMigrationTests</c>-style harness).
/// </summary>
public class ConcurrentIndexTests
{
    private static AddIndex MakeChange(bool isConcurrent) =>
        new("Orders", new IndexSchema("IX_Orders_Email", ImmutableArray.Create("Email"), IsUnique: true),
            IsConcurrent: isConcurrent);

    [Fact]
    public void PostgreSql_Concurrent_EmitsConcurrentlyKeyword()
    {
        var sql = new PostgreSqlMigrationGenerator().GenerateChangeScript(MakeChange(true));

        sql.Should().Contain("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS");
    }

    [Fact]
    public void PostgreSql_NotConcurrent_NoConcurrentlyKeyword()
    {
        var sql = new PostgreSqlMigrationGenerator().GenerateChangeScript(MakeChange(false));

        sql.Should().NotContain("CONCURRENTLY");
        sql.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS");
    }

    [Fact]
    public void SqlServer_Concurrent_EmitsOnlineWithClause()
    {
        var sql = new SqlServerMigrationGenerator().GenerateChangeScript(MakeChange(true));

        sql.Should().Contain("WITH (ONLINE = ON)");
    }

    [Fact]
    public void SqlServer_NotConcurrent_NoOnlineClause()
    {
        var sql = new SqlServerMigrationGenerator().GenerateChangeScript(MakeChange(false));

        sql.Should().NotContain("ONLINE = ON");
    }

    [Fact]
    public void Sqlite_IgnoresConcurrentFlag()
    {
        var concurrent = new SqliteMigrationGenerator().GenerateChangeScript(MakeChange(true));
        var inline = new SqliteMigrationGenerator().GenerateChangeScript(MakeChange(false));

        concurrent.Should().Be(inline, "SQLite has no concurrent / online index creation");
        concurrent.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS");
    }

    [Fact]
    public void MigrationsBuilder_UseConcurrentIndexes_SetsOption()
    {
        var services = new ServiceCollection();
        var builder = new PragmaticBuilderMock();
        builder.Services.Returns(services);

        builder.UsePragmaticMigrations(m => m.UseConcurrentIndexes());

        var options = services.BuildServiceProvider().GetRequiredService<MigrationOptions>();
        options.ConcurrentIndexes.Should().BeTrue();
    }
}
