#pragma warning disable CA2007

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Core.Tests.Unit.Diff;

/// <summary>
///     Tests that the diff engine matches FK and index by structure, not by name.
/// </summary>
public class FkNormalizationTests
{
    private readonly ISchemaDiffEngine _engine = new SchemaDiffEngine();

    [Fact]
    public void ForeignKeys_SameStructureDifferentName_NoChanges()
    {
        var desired = new SchemaVersion(ImmutableArray.Create(
            new TableSchema("Orders", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("UserId", "uuid", false, false)),
                [],
                ImmutableArray.Create(
                    new ForeignKeySchema("FK_Orders_UserId_Users", "UserId", "Users", "Id", ReferentialAction.Cascade)))));

        var current = new SchemaVersion(ImmutableArray.Create(
            new TableSchema("Orders", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("UserId", "uuid", false, false)),
                [],
                ImmutableArray.Create(
                    new ForeignKeySchema("FK_Orders_Users_UserId", "UserId", "Users", "Id", ReferentialAction.Cascade)))));

        var diff = _engine.ComputeDiff(desired, current);
        diff.Changes.Where(c => c is AddForeignKey or DropForeignKey).Should().BeEmpty();
    }

    [Fact]
    public void ForeignKeys_DifferentOnDelete_GeneratesDropAndAdd()
    {
        var desired = new SchemaVersion(ImmutableArray.Create(
            new TableSchema("Orders", null,
                ImmutableArray.Create(new ColumnSchema("Id", "uuid", false, true), new ColumnSchema("UserId", "uuid", false, false)),
                [],
                ImmutableArray.Create(new ForeignKeySchema("FK1", "UserId", "Users", "Id", ReferentialAction.SetNull)))));

        var current = new SchemaVersion(ImmutableArray.Create(
            new TableSchema("Orders", null,
                ImmutableArray.Create(new ColumnSchema("Id", "uuid", false, true), new ColumnSchema("UserId", "uuid", false, false)),
                [],
                ImmutableArray.Create(new ForeignKeySchema("FK1", "UserId", "Users", "Id", ReferentialAction.Cascade)))));

        var diff = _engine.ComputeDiff(desired, current);
        diff.Changes.Should().Contain(c => c is DropForeignKey);
        diff.Changes.Should().Contain(c => c is AddForeignKey);
    }

    /// <summary>
    ///     <c>Restrict</c> is written as <c>NO ACTION</c> by every generator, so <c>NO ACTION</c> is what the
    ///     database reads back: the same key, not a change.
    /// </summary>
    [Fact]
    public void ForeignKeys_RestrictDesired_NoActionInTheDatabase_NoChanges()
    {
        var desired = new SchemaVersion(ImmutableArray.Create(
            new TableSchema("Orders", null,
                ImmutableArray.Create(new ColumnSchema("Id", "uuid", false, true), new ColumnSchema("UserId", "uuid", false, false)),
                [],
                ImmutableArray.Create(new ForeignKeySchema("FK1", "UserId", "Users", "Id", ReferentialAction.Restrict)))));

        var current = new SchemaVersion(ImmutableArray.Create(
            new TableSchema("Orders", null,
                ImmutableArray.Create(new ColumnSchema("Id", "uuid", false, true), new ColumnSchema("UserId", "uuid", false, false)),
                [],
                ImmutableArray.Create(new ForeignKeySchema("FK1", "UserId", "Users", "Id", ReferentialAction.NoAction)))));

        var diff = _engine.ComputeDiff(desired, current);
        diff.Changes.Where(c => c is AddForeignKey or DropForeignKey).Should().BeEmpty();
    }

    /// <summary>The control: <c>Restrict</c> against a key that cascades is still a change.</summary>
    [Fact]
    public void ForeignKeys_RestrictDesired_CascadeInTheDatabase_GeneratesDropAndAdd()
    {
        var desired = new SchemaVersion(ImmutableArray.Create(
            new TableSchema("Orders", null,
                ImmutableArray.Create(new ColumnSchema("Id", "uuid", false, true), new ColumnSchema("UserId", "uuid", false, false)),
                [],
                ImmutableArray.Create(new ForeignKeySchema("FK1", "UserId", "Users", "Id", ReferentialAction.Restrict)))));

        var current = new SchemaVersion(ImmutableArray.Create(
            new TableSchema("Orders", null,
                ImmutableArray.Create(new ColumnSchema("Id", "uuid", false, true), new ColumnSchema("UserId", "uuid", false, false)),
                [],
                ImmutableArray.Create(new ForeignKeySchema("FK1", "UserId", "Users", "Id", ReferentialAction.Cascade)))));

        var diff = _engine.ComputeDiff(desired, current);
        diff.Changes.Should().Contain(c => c is DropForeignKey);
        diff.Changes.Should().Contain(c => c is AddForeignKey);
    }

    /// <summary>
    ///     PostgreSQL hands a partial index's predicate back wrapped in parentheses: the same filter, not a
    ///     change (otherwise the saga table's filtered index is dropped and recreated on every start).
    /// </summary>
    [Fact]
    public void Indexes_FilterWrappedInParentheses_NoChanges()
    {
        var diff = _engine.ComputeDiff(WithFilteredIndex("\"Status\" = 0"), WithFilteredIndex("(\"Status\" = 0)"));

        diff.Changes.Where(c => c is AddIndex or DropIndex).Should().BeEmpty();
    }

    /// <summary>The control: a different predicate is still a change.</summary>
    [Fact]
    public void Indexes_DifferentFilter_GeneratesDropAndAdd()
    {
        var diff = _engine.ComputeDiff(WithFilteredIndex("\"Status\" = 0"), WithFilteredIndex("(\"Status\" = 1)"));

        diff.Changes.Should().Contain(c => c is DropIndex);
        diff.Changes.Should().Contain(c => c is AddIndex);
    }

    private static SchemaVersion WithFilteredIndex(string filter)
        => new(ImmutableArray.Create(
            new TableSchema("Sagas", null,
                ImmutableArray.Create(new ColumnSchema("Id", "uuid", false, true), new ColumnSchema("Status", "integer", false, false)),
                ImmutableArray.Create(new IndexSchema("IX_Sagas_Status", ImmutableArray.Create("Status"), true, filter)),
                [])));

    [Fact]
    public void Indexes_SameColumnsDifferentName_NoChanges()
    {
        var desired = new SchemaVersion(ImmutableArray.Create(
            new TableSchema("Users", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("Email", "text", false, false)),
                ImmutableArray.Create(
                    new IndexSchema("IX_Users_Email", ImmutableArray.Create("Email"), true)),
                [])));

        var current = new SchemaVersion(ImmutableArray.Create(
            new TableSchema("Users", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("Email", "text", false, false)),
                ImmutableArray.Create(
                    new IndexSchema("IX_Users_Email_UniqueIdx", ImmutableArray.Create("Email"), true)),
                [])));

        var diff = _engine.ComputeDiff(desired, current);
        diff.Changes.Where(c => c is AddIndex or DropIndex).Should().BeEmpty();
    }
}
