#pragma warning disable CA2007

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Sql;

/// <summary>
///     Tests the per-change GenerateChangeScript API across all 3 providers.
/// </summary>
public class GenerateChangeScriptTests
{
    private static readonly ISqlMigrationGenerator[] Generators =
    [
        new PostgreSqlMigrationGenerator(),
        new SqlServerMigrationGenerator(),
        new SqliteMigrationGenerator()
    ];

    [Fact]
    public void GenerateChangeScript_CreateTable_AllProviders()
    {
        var change = new CreateTable(new TableSchema("Items", null,
            ImmutableArray.Create(new ColumnSchema("Id", "TEXT", false, true)),
            [], []));

        foreach (var gen in Generators)
        {
            var sql = gen.GenerateChangeScript(change);
            sql.Should().NotBeNullOrWhiteSpace($"{gen.ProviderName} should produce SQL");
            sql.Should().Contain("Items", $"{gen.ProviderName} should reference table name");
        }
    }

    [Fact]
    public void GenerateChangeScript_AddColumn_AllProviders()
    {
        var change = new AddColumn("Users", new ColumnSchema("Email", "TEXT", true, false));
        foreach (var gen in Generators)
        {
            var sql = gen.GenerateChangeScript(change);
            sql.Should().Contain("Email");
        }
    }

    [Fact]
    public void GenerateChangeScript_RenameColumn_AllProviders()
    {
        var change = new RenameColumn("Users", "OldName", "NewName");
        foreach (var gen in Generators)
        {
            var sql = gen.GenerateChangeScript(change);
            sql.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void GenerateChangeScript_AlterColumnNullability_IncludesType()
    {
        var change = new AlterColumnNullability("Orders", "Amount", true, "decimal(18,2)");
        var sqlServer = new SqlServerMigrationGenerator();
        var sql = sqlServer.GenerateChangeScript(change);
        sql.Should().Contain("decimal(18,2)"); // SQL Server requires type in ALTER COLUMN
    }

    [Fact]
    public void GenerateChangeScript_AddForeignKey_AllProviders()
    {
        var change = new AddForeignKey("Orders",
            new ForeignKeySchema("FK_Orders_UserId_Users", "UserId", "Users", "Id", ReferentialAction.Cascade));

        foreach (var gen in Generators)
        {
            // A provider either renders the change directly, or declares it needs a table rebuild.
            // What it must never do is return SQL that quietly does nothing.
            if (gen.GetRebuildTableName(change) is { } rebuildTable)
            {
                rebuildTable.Should().Be("Orders");
                continue;
            }

            var sql = gen.GenerateChangeScript(change);
            sql.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void GenerateChangeScript_DropTable_IsBreaking()
    {
        var change = new DropTable("OldTable");
        change.IsBreaking.Should().BeTrue();

        foreach (var gen in Generators)
        {
            var sql = gen.GenerateChangeScript(change);
            sql.Should().NotBeNullOrWhiteSpace();
        }
    }
}
