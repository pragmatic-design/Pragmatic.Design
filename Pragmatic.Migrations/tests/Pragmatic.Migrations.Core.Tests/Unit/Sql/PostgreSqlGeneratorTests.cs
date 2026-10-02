using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Sql;

public class PostgreSqlGeneratorTests
{
    private readonly PostgreSqlMigrationGenerator _gen = new();

    [Fact]
    public void GenerateScript_CreateTable_ProducesCreateTableIfNotExists()
    {
        var diff = new SchemaDiff(
        [
            new CreateTable(new TableSchema("Users", "public",
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("Name", "text", false, false),
                    new ColumnSchema("Email", "varchar(256)", true, false)
                ], [], []))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS");
        sql.Should().Contain("\"public\".\"Users\"");
        sql.Should().Contain("\"Id\" uuid NOT NULL");
        sql.Should().Contain("\"Email\" varchar(256) NULL");
        sql.Should().Contain("PRIMARY KEY");
    }

    [Fact]
    public void GenerateScript_AddColumn_ProducesIdempotentDoBlock()
    {
        var diff = new SchemaDiff(
        [
            new AddColumn("Users", new ColumnSchema("TenantId", "varchar(128)", true, false))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("DO $$ BEGIN");
        sql.Should().Contain("IF NOT EXISTS");
        sql.Should().Contain("information_schema.columns");
        sql.Should().Contain("ADD COLUMN");
        sql.Should().Contain("\"TenantId\" varchar(128) NULL");
    }

    [Fact]
    public void GenerateScript_RenameColumn_ProducesIdempotentRename()
    {
        var diff = new SchemaDiff(
        [
            new RenameColumn("Guests", "FirstName", "GivenName")
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("RENAME COLUMN");
        sql.Should().Contain("\"FirstName\"");
        sql.Should().Contain("\"GivenName\"");
        sql.Should().Contain("IF EXISTS");
        sql.Should().Contain("AND NOT EXISTS");
    }

    [Fact]
    public void GenerateScript_AddIndex_ProducesCreateIndexIfNotExists()
    {
        var diff = new SchemaDiff(
        [
            new AddIndex("Users", new IndexSchema("IX_Users_Email", ImmutableArray.Create("Email"), true, "\"IsDeleted\" = false"))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS");
        sql.Should().Contain("\"IX_Users_Email\"");
        sql.Should().Contain("WHERE \"IsDeleted\" = false");
    }

    [Fact]
    public void GenerateScript_AddForeignKey_ProducesIdempotentConstraint()
    {
        var diff = new SchemaDiff(
        [
            new AddForeignKey("Orders", new ForeignKeySchema("FK_Orders_UserId_Users", "UserId", "Users", "Id", ReferentialAction.Cascade))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("ADD CONSTRAINT");
        sql.Should().Contain("FOREIGN KEY");
        sql.Should().Contain("REFERENCES");
        sql.Should().Contain("ON DELETE CASCADE");
    }

    [Fact]
    public void GenerateAuditTableDdl_ProducesValidPgSql()
    {
        var sql = _gen.GenerateAuditTableDdl();

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"public\".\"__PragmaticSchema\"");
        sql.Should().Contain("SERIAL PRIMARY KEY");
        sql.Should().Contain("text NOT NULL");
        sql.Should().Contain("timestamptz");
    }

    [Fact]
    public void GenerateAuditTableDdl_CustomName_CreatesThatTable()
    {
        // The runner inserts into MigrationOptions.AuditTableName; creating a differently-named
        // table here would leave the INSERT pointing at something that was never created.
        var sql = _gen.GenerateAuditTableDdl("_MyHistory");

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"public\".\"_MyHistory\"");
        sql.Should().Contain("\"IX__MyHistory_AppliedAt\"");
        sql.Should().NotContain("__PragmaticSchema");
    }

    [Fact]
    public void GenerateDropIndex_QualifiesTheIndexWithItsSchema()
    {
        // An unqualified DROP INDEX resolves through search_path and silently misses an index that
        // lives in another schema — the stale index would then be re-proposed on every run.
        var sql = _gen.GenerateChangeScript(new DropIndex("Orders", "IX_Orders_Code", "reporting"));

        sql.Should().Contain("DROP INDEX IF EXISTS \"reporting\".\"IX_Orders_Code\"");
    }

    [Fact]
    public void ProviderName_IsPostgreSql()
    {
        _gen.ProviderName.Should().Be("PostgreSql");
    }
}
