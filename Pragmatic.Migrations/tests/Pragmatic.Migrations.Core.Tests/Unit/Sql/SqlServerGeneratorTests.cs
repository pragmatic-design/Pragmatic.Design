using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Sql;

public class SqlServerGeneratorTests
{
    private readonly SqlServerMigrationGenerator _gen = new();

    [Fact]
    public void GenerateScript_CreateTable_ProducesIfNotExistsPattern()
    {
        var diff = new SchemaDiff(
        [
            new CreateTable(new TableSchema("Users", "dbo",
                [
                    new ColumnSchema("Id", "uniqueidentifier", false, true),
                    new ColumnSchema("Name", "nvarchar(256)", false, false)
                ], [], []))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("IF NOT EXISTS (SELECT 1 FROM sys.tables");
        sql.Should().Contain("[dbo].[Users]");
        sql.Should().Contain("[Id] uniqueidentifier NOT NULL");
        sql.Should().Contain("[PK_Users] PRIMARY KEY");
    }

    [Fact]
    public void GenerateScript_AddColumn_UsesSysColumns()
    {
        var diff = new SchemaDiff(
        [
            new AddColumn("Users", new ColumnSchema("Email", "nvarchar(512)", true, false))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("sys.columns");
        sql.Should().Contain("OBJECT_ID");
        sql.Should().Contain("ALTER TABLE");
        sql.Should().Contain("[Email] nvarchar(512) NULL");
    }

    [Fact]
    public void GenerateScript_RenameColumn_UsesSpRename()
    {
        var diff = new SchemaDiff(
        [
            new RenameColumn("Guests", "FirstName", "GivenName")
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("sp_rename");
        sql.Should().Contain("'COLUMN'");
    }

    [Fact]
    public void GenerateAuditTableDdl_ProducesValidTSql()
    {
        var sql = _gen.GenerateAuditTableDdl();

        sql.Should().Contain("[dbo].[__PragmaticSchema]");
        sql.Should().Contain("IDENTITY(1,1)");
        sql.Should().Contain("datetimeoffset");
        sql.Should().Contain("SYSUTCDATETIME()");
    }

    [Fact]
    public void ProviderName_IsSqlServer()
    {
        _gen.ProviderName.Should().Be("SqlServer");
    }

    [Theory]
    [InlineData(true, "NULL")]
    [InlineData(false, "NOT NULL")]
    public void GenerateAlterColumnType_RestatesNullability(bool isNullable, string expectedSpec)
    {
        // SQL Server's ALTER COLUMN restates the whole definition: omitting NULL/NOT NULL means
        // NULL, so a plain type change would silently drop an existing NOT NULL constraint.
        var change = new AlterColumnType("Users", "Email", "nvarchar(100)", "nvarchar(200)",
            IsNarrowing: false, SchemaName: null, IsNullable: isNullable);

        var sql = _gen.GenerateChangeScript(change);

        sql.Should().Contain($"ALTER COLUMN [Email] nvarchar(200) {expectedSpec};");
    }

    [Fact]
    public void GenerateAlterColumnType_UnknownNullability_Throws()
    {
        var change = new AlterColumnType("Users", "Email", "nvarchar(100)", "nvarchar(200)", IsNarrowing: false);

        var act = () => _gen.GenerateChangeScript(change);

        act.Should().Throw<InvalidOperationException>().WithMessage("*nullability is unknown*");
    }

    [Fact]
    public void GenerateAlterColumnDefault_DropsTheExistingConstraintBeforeAdding()
    {
        // ADD DEFAULT on a column that already has one fails with Msg 1781, and AlterColumnDefault
        // is emitted precisely when a default CHANGED — so the old one is nearly always present.
        var change = new AlterColumnDefault("Users", "IsActive", "((0))", "((1))");

        var sql = _gen.GenerateChangeScript(change);

        sql.Should().Contain("sys.default_constraints");
        sql.Should().Contain("DROP CONSTRAINT [");
        sql.Should().Contain("ADD DEFAULT ((1)) FOR [IsActive];");
        sql.IndexOf("DROP CONSTRAINT", StringComparison.Ordinal)
            .Should().BeLessThan(sql.IndexOf("ADD DEFAULT", StringComparison.Ordinal),
                "the old constraint must be gone before the new default is added");
    }

    [Fact]
    public void GenerateAlterColumnDefault_RemovingTheDefault_OnlyDrops()
    {
        var change = new AlterColumnDefault("Users", "IsActive", "((0))", null);

        var sql = _gen.GenerateChangeScript(change);

        sql.Should().Contain("DROP CONSTRAINT [");
        sql.Should().NotContain("ADD DEFAULT");
    }

    [Fact]
    public void GenerateAuditTableDdl_CustomName_CreatesThatTable()
    {
        var sql = _gen.GenerateAuditTableDdl("_MyHistory");

        sql.Should().Contain("CREATE TABLE [dbo].[_MyHistory]");
        sql.Should().Contain("[IX__MyHistory_AppliedAt]");
        sql.Should().NotContain("__PragmaticSchema");
    }
}
