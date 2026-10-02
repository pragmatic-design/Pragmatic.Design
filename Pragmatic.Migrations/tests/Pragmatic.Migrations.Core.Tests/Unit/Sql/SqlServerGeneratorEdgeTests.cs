using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Sql;

/// <summary>
///     Edge-case coverage for <see cref="SqlServerMigrationGenerator"/>: the typed
///     <see cref="ReferentialAction"/> ON DELETE mapping, the configurable default schema, and the
///     no-fabricated-<c>nvarchar(max)</c> safety fix on nullability changes.
/// </summary>
public class SqlServerGeneratorEdgeTests
{
    private readonly SqlServerMigrationGenerator _gen = new();

    [Theory]
    [InlineData(ReferentialAction.Cascade, "ON DELETE CASCADE")]
    [InlineData(ReferentialAction.SetNull, "ON DELETE SET NULL")]
    [InlineData(ReferentialAction.NoAction, "ON DELETE NO ACTION")]
    [InlineData(ReferentialAction.Restrict, "ON DELETE NO ACTION")] // SQL Server has no RESTRICT keyword
    public void GenerateScript_AddForeignKey_MapsReferentialActionToOnDeleteClause(
        ReferentialAction action, string expectedClause)
    {
        var diff = new SchemaDiff(
        [
            new AddForeignKey("Orders", new ForeignKeySchema("FK_O_U", "UserId", "Users", "Id", action))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain(expectedClause);
    }

    [Fact]
    public void GenerateScript_AddColumn_EscapesSingleQuotesInExistenceCheckLiteral()
    {
        // The existence-check literal `name = '...'` must escape single quotes
        // (match the Postgres / AlterColumnDefault treatment), or a column name containing an
        // apostrophe breaks the metadata query and the column is added twice.
        var diff = new SchemaDiff(
        [
            new AddColumn("Books", new ColumnSchema("O'Brien", "nvarchar(64)", IsNullable: true, IsPrimaryKey: false))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("name = 'O''Brien'");
        sql.Should().NotContain("name = 'O'Brien'");
    }

    [Fact]
    public void GenerateScript_DropColumn_EscapesSingleQuotesInExistenceCheckLiteral()
    {
        var diff = new SchemaDiff([new DropColumn("Books", "O'Brien")], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("name = 'O''Brien'");
    }

    [Fact]
    public void GenerateScript_RenameColumn_EscapesSingleQuotesInBothExistenceCheckLiterals()
    {
        var diff = new SchemaDiff([new RenameColumn("Books", "O'Brien", "O'Reilly")], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("name = 'O''Brien'");
        sql.Should().Contain("name = 'O''Reilly'");
    }

    [Fact]
    public void GenerateScript_AlterColumnNullability_WithEmptyColumnType_ThrowsInsteadOfFabricatingNvarcharMax()
    {
        // With the type unknown, fabricating nvarchar(max) would silently rewrite the column, so a hard
        // error is raised instead.
        var diff = new SchemaDiff(
        [
            new AlterColumnNullability("Users", "Name", NewIsNullable: true, ColumnType: "")
        ], false);

        var act = () => _gen.GenerateScript(diff);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Users.Name*");
    }

    [Fact]
    public void GenerateScript_AlterColumnNullability_WithKnownType_RestatesTypeAndNullSpec()
    {
        var diff = new SchemaDiff(
        [
            new AlterColumnNullability("Users", "Email", NewIsNullable: false, ColumnType: "nvarchar(256)")
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().NotContain("nvarchar(max)");
        sql.Should().Contain("ALTER COLUMN [Email] nvarchar(256) NOT NULL");
    }

    [Fact]
    public void GenerateScript_CreateTable_WithCustomDefaultSchema_QualifiesWithThatSchema()
    {
        var gen = new SqlServerMigrationGenerator { DefaultSchema = "app" };
        var diff = new SchemaDiff(
        [
            new CreateTable(new TableSchema("Users", null,
                [new ColumnSchema("Id", "int", false, true)], [], []))
        ], false);

        var sql = gen.GenerateScript(diff);

        sql.Should().Contain("[app].[Users]");
        sql.Should().NotContain("[dbo].[Users]");
    }
}
