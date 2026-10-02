using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Core.Tests.Unit.Diff;

/// <summary>
///     Pairing tables across the two schemas has to tolerate one side omitting the schema
///     qualifier — the generator usually does — without ever matching a table to a same-named one
///     from a different schema. The previous "null matches anything" comparer was not transitive,
///     so a Dictionary resolved such a lookup to whichever entry sat first in the bucket, and the
///     result depended on table order.
/// </summary>
public class TableMatchingTests
{
    private readonly SchemaDiffEngine _engine = new();

    private static TableSchema Table(string name, string? schema, params string[] columns) =>
        new(name, schema, [.. columns.Select(c => new ColumnSchema(c, "text", true, false))], [], []);

    [Fact]
    public void UnqualifiedDesired_MatchesTheSingleTableWithThatName()
    {
        var desired = new SchemaVersion([Table("Orders", null, "Id", "Note")]);
        var current = new SchemaVersion([Table("Orders", "public", "Id")]);

        var diff = _engine.ComputeDiff(desired, current);

        diff.Changes.Should().ContainSingle().Which.Should().BeOfType<AddColumn>();
    }

    [Fact]
    public void UnqualifiedDesired_AmbiguousAcrossSchemas_DoesNotGuess()
    {
        // "Orders" exists in two schemas and the desired side names neither. Matching one at
        // random would diff against an arbitrary table; the honest outcome is to treat the desired
        // table as new, which surfaces in the plan instead of silently altering the wrong one.
        var desired = new SchemaVersion([Table("Orders", null, "Id")]);
        var current = new SchemaVersion([Table("Orders", "public", "Id"), Table("Orders", "archive", "Id")]);

        var diff = _engine.ComputeDiff(desired, current);

        diff.Changes.OfType<CreateTable>().Should().ContainSingle(c => c.Table.Name == "Orders");
        diff.Changes.OfType<AlterColumnType>().Should().BeEmpty();
    }

    [Fact]
    public void MatchingIsIndependentOfTableOrder()
    {
        var desired = new SchemaVersion([Table("Orders", null, "Id")]);
        var forward = new SchemaVersion([Table("Orders", "public", "Id"), Table("Orders", "archive", "Id")]);
        var reversed = new SchemaVersion([Table("Orders", "archive", "Id"), Table("Orders", "public", "Id")]);

        var a = _engine.ComputeDiff(desired, forward);
        var b = _engine.ComputeDiff(desired, reversed);

        a.Changes.Select(c => c.Description).Order()
            .Should().Equal(b.Changes.Select(c => c.Description).Order(),
                "the outcome must not depend on the order tables came back in");
    }

    [Fact]
    public void QualifiedTables_InDifferentSchemas_AreDistinct()
    {
        var desired = new SchemaVersion([Table("Orders", "public", "Id"), Table("Orders", "archive", "Id")]);
        var current = new SchemaVersion([Table("Orders", "public", "Id")]);

        var diff = _engine.ComputeDiff(desired, current);

        diff.Changes.OfType<CreateTable>().Should().ContainSingle()
            .Which.Table.SchemaName.Should().Be("archive");
    }

    [Fact]
    public void QualifiedDesired_MatchesAnUnqualifiedCurrentTable()
    {
        // The reverse of the common case: SQLite introspection reports no schema at all.
        var desired = new SchemaVersion([Table("Orders", "main", "Id", "Note")]);
        var current = new SchemaVersion([Table("Orders", null, "Id")]);

        var diff = _engine.ComputeDiff(desired, current);

        diff.Changes.Should().ContainSingle().Which.Should().BeOfType<AddColumn>();
    }
}
