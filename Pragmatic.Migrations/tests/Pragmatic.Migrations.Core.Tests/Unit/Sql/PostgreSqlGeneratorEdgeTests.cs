using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Sql;

/// <summary>
///     Edge-case coverage for <see cref="PostgreSqlMigrationGenerator"/>: the typed
///     <see cref="ReferentialAction"/> ON DELETE mapping and the configurable default schema.
/// </summary>
public class PostgreSqlGeneratorEdgeTests
{
    private readonly PostgreSqlMigrationGenerator _gen = new();

    [Theory]
    [InlineData(ReferentialAction.Cascade, "ON DELETE CASCADE")]
    [InlineData(ReferentialAction.SetNull, "ON DELETE SET NULL")]
    [InlineData(ReferentialAction.NoAction, "ON DELETE NO ACTION")]
    [InlineData(ReferentialAction.Restrict, "ON DELETE NO ACTION")]
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
    public void GenerateScript_CreateTable_DefaultsToPublicSchema()
    {
        var diff = new SchemaDiff(
        [
            new CreateTable(new TableSchema("Users", null,
                [new ColumnSchema("Id", "uuid", false, true)], [], []))
        ], false);

        var sql = _gen.GenerateScript(diff);

        sql.Should().Contain("\"public\".\"Users\"");
    }

    [Fact]
    public void GenerateScript_CreateTable_WithCustomDefaultSchema_QualifiesWithThatSchema()
    {
        var gen = new PostgreSqlMigrationGenerator { DefaultSchema = "tenant" };
        var diff = new SchemaDiff(
        [
            new CreateTable(new TableSchema("Users", null,
                [new ColumnSchema("Id", "uuid", false, true)], [], []))
        ], false);

        var sql = gen.GenerateScript(diff);

        sql.Should().Contain("\"tenant\".\"Users\"");
    }
}
