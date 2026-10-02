using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Sql;

public class SqliteGeneratorTests
{
    private readonly SqliteMigrationGenerator _gen = new();

    [Fact]
    public void GenerateScript_CreateTable_ProducesCreateTableIfNotExists()
    {
        var diff = new SchemaDiff(
        [
            new CreateTable(new TableSchema("Users", null,
                [
                    new ColumnSchema("Id", "TEXT", false, true),
                    new ColumnSchema("Name", "TEXT", false, false)
                ], [], []))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS");
        sql.Should().Contain("\"Users\"");
        sql.Should().Contain("\"Id\" TEXT NOT NULL");
    }

    [Fact]
    public void GenerateScript_AddColumn_ProducesSimpleAlter()
    {
        var diff = new SchemaDiff(
        [
            new AddColumn("Users", new ColumnSchema("Email", "TEXT", true, false))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("ALTER TABLE \"Users\" ADD COLUMN \"Email\" TEXT NULL");
    }

    [Fact]
    public void GenerateScript_RenameColumn_ProducesRenameColumn()
    {
        var diff = new SchemaDiff(
        [
            new RenameColumn("Guests", "FirstName", "GivenName")
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("RENAME COLUMN");
        sql.Should().Contain("\"FirstName\"");
        sql.Should().Contain("\"GivenName\"");
    }

    [Fact]
    public void GetRebuildTableName_AlterColumnType_RequiresRebuild()
    {
        var change = new AlterColumnType("Orders", "Amount", "INTEGER", "REAL", false);

        _gen.GetRebuildTableName(change).Should().Be("Orders");
    }

    [Fact]
    public void GetRebuildTableName_AddForeignKey_RequiresRebuild()
    {
        var change = new AddForeignKey("Orders",
            new ForeignKeySchema("FK_Test", "UserId", "Users", "Id", ReferentialAction.NoAction));

        _gen.GetRebuildTableName(change).Should().Be("Orders");
    }

    [Fact]
    public void GetRebuildTableName_AddColumn_AppliesDirectly()
    {
        var change = new AddColumn("Orders", new ColumnSchema("Note", "TEXT", true, false));

        _gen.GetRebuildTableName(change).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(RebuildOnlyChanges))]
    public void GenerateChangeScript_RebuildOnlyChange_Throws(SchemaChange change)
    {
        // Must NOT return a statement that quietly does nothing: a previous version emitted
        // `SELECT 1/0` expecting SQLite to abort, but SQLite evaluates it to NULL — the change
        // vanished and the migration reported success on an unchanged schema.
        var act = () => _gen.GenerateChangeScript(change);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*table rebuild*");
    }

    public static TheoryData<SchemaChange> RebuildOnlyChanges() =>
    [
        new AlterColumnType("Orders", "Amount", "INTEGER", "REAL", false),
        new AlterColumnNullability("Orders", "Note", true, "TEXT"),
        new AlterColumnDefault("Orders", "Status", "'a'", "'b'"),
        new AddForeignKey("Orders", new ForeignKeySchema("FK_Test", "UserId", "Users", "Id", ReferentialAction.NoAction)),
        new DropForeignKey("Orders", "FK_Test")
    ];

    [Fact]
    public void GenerateScript_ForeignKeyOnTableCreatedInSameDiff_IsCoveredByCreateTable()
    {
        // SQLite puts FKs inline in CREATE TABLE, so the separate AddForeignKey the diff engine
        // emits for a brand-new table is already satisfied — and must not trigger a rebuild of a
        // table that did not exist a moment ago.
        var orders = new TableSchema("Orders", null,
            [new ColumnSchema("Id", "TEXT", false, true), new ColumnSchema("UserId", "TEXT", false, false)],
            [],
            [new ForeignKeySchema("FK_Test", "UserId", "Users", "Id", ReferentialAction.NoAction)]);

        var diff = new SchemaDiff(
        [
            new CreateTable(orders),
            new AddForeignKey("Orders", orders.ForeignKeys[0])
        ], false, new SchemaVersion([orders]));

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"Orders\"");
        sql.Should().Contain("FOREIGN KEY (\"UserId\")");
        sql.Should().Contain("already part of CREATE TABLE");
        sql.Should().NotContain("RENAME TO");
    }

    [Fact]
    public void GenerateScript_NullabilityChange_EmitsTableRebuild()
    {
        var orders = new TableSchema("Orders", null,
            [new ColumnSchema("Id", "TEXT", false, true), new ColumnSchema("Note", "TEXT", true, false)],
            [], []);

        var diff = new SchemaDiff(
        [
            new AlterColumnNullability("Orders", "Note", true, "TEXT")
        ], false, new SchemaVersion([orders]));

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("PRAGMA defer_foreign_keys = ON;");
        sql.Should().Contain("ALTER TABLE \"Orders\" RENAME TO \"__Orders_rebuild\";");
        sql.Should().Contain("INSERT INTO \"Orders\"");
        sql.Should().Contain("DROP TABLE \"__Orders_rebuild\";");
        // PRAGMA foreign_keys is a documented no-op inside a transaction — the runner owns one.
        sql.Should().NotContain("PRAGMA foreign_keys");
    }

    [Fact]
    public void GenerateTableRebuild_OnlyCopiesColumnsPresentInBothSchemas()
    {
        var desired = new TableSchema("Orders", null,
            [
                new ColumnSchema("Id", "TEXT", false, true),
                new ColumnSchema("Note", "TEXT", true, false),
                new ColumnSchema("AddedNow", "TEXT", true, false)
            ], [], []);

        var sql = _gen.GenerateTableRebuild(desired, ["Id", "Note"]);

        // AddedNow does not exist in the source table — selecting it would fail.
        sql.Should().Contain("INSERT INTO \"Orders\" (\"Id\", \"Note\") SELECT \"Id\", \"Note\"");
        sql.Should().NotContain("AddedNow\" FROM");
    }

    [Fact]
    public void GenerateAuditTableDdl_CustomName_CreatesThatTable()
    {
        var sql = _gen.GenerateAuditTableDdl("_MyHistory");

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"_MyHistory\"");
        sql.Should().Contain("\"IX__MyHistory_AppliedAt\"");
        sql.Should().NotContain("__PragmaticSchema");
    }

    [Fact]
    public void GenerateAuditTableDdl_ProducesValidSqlite()
    {
        var sql = _gen.GenerateAuditTableDdl();

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"__PragmaticSchema\"");
        sql.Should().Contain("AUTOINCREMENT");
        sql.Should().Contain("datetime('now')");
    }

    [Fact]
    public void ProviderName_IsSqlite()
    {
        _gen.ProviderName.Should().Be("Sqlite");
    }
}
