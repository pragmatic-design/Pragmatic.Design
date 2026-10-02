using System.Reflection;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Core.Tests.Unit.Diff;

/// <summary>
///     Every schema change targets a table, and several parts of the pipeline group changes by it —
///     most importantly the SQLite table-rebuild consolidation, which only intercepts a change it
///     can attribute to a table. A change missing from <see cref="SchemaChangeTarget"/> silently
///     falls through to the per-change path and blows up there, so a new change type (such as
///     <see cref="AlterPrimaryKey"/>) needs the mapping updated with it.
/// </summary>
public class SchemaChangeTargetTests
{
    public static TheoryData<SchemaChange> AllChangeTypes()
    {
        var table = new TableSchema("Orders", null, [new ColumnSchema("Id", "uuid", false, true)], [], []);
        var column = new ColumnSchema("Note", "text", true, false);
        var index = new IndexSchema("IX", ["Note"]);
        var fk = new ForeignKeySchema("FK", "Id", "Customers", "Id", ReferentialAction.NoAction);
        var check = new CheckConstraintSchema("CK_Orders_Positive", "\"Total\" >= 0");

        return
        [
            new CreateTable(table),
            new DropTable("Orders"),
            new AddColumn("Orders", column),
            new DropColumn("Orders", "Note"),
            new RenameColumn("Orders", "Old", "New"),
            new AlterColumnType("Orders", "Note", "text", "varchar(10)", true),
            new AlterColumnNullability("Orders", "Note", false, "text"),
            new AlterColumnDefault("Orders", "Note", null, "'x'"),
            new AlterPrimaryKey("Orders", [], ["Id"]),
            new AddIndex("Orders", index),
            new DropIndex("Orders", "IX"),
            new AddForeignKey("Orders", fk),
            new AddCheckConstraint("Orders", check),
            new DropCheckConstraint("Orders", "CK_Orders_Positive"),
            new DropForeignKey("Orders", "FK")
        ];
    }

    [Theory]
    [MemberData(nameof(AllChangeTypes))]
    public void TableNameOf_ResolvesEveryChangeType(SchemaChange change) =>
        SchemaChangeTarget.TableNameOf(change).Should().Be("Orders");

    [Fact]
    public void EveryConcreteChangeType_IsCovered()
    {
        // Guards against the next change type being added without a case: reflection finds them
        // all, the theory above only covers the ones someone remembered to list.
        var declared = typeof(SchemaChange).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false } && t.IsSubclassOf(typeof(SchemaChange)))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        var covered = AllChangeTypes()
            .Cast<object[]>()
            .Select(row => row[0].GetType().Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        covered.Should().Equal(declared);
    }
}
