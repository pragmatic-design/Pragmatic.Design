using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Cli.Snapshot;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Core.Tests.Unit.Cli;

/// <summary>
///     The committed schema snapshot must be byte-stable: same schema → identical JSON,
///     order-independent, LF-only. Otherwise the CI git-diff gate produces false drift.
/// </summary>
public class SchemaSnapshotSerializerTests
{
    private static SchemaVersion Schema(params TableSchema[] tables)
        => new([.. tables], DatabaseName: "Test", ProviderName: "Sqlite");

    private static TableSchema Table(string name, ImmutableArray<IndexSchema> indexes = default)
        => new(name, "public",
            ImmutableArray.Create(new ColumnSchema("Id", "uuid", false, true)),
            indexes.IsDefault ? [] : indexes,
            []);

    [Fact]
    public void Serialize_SameSchema_ProducesIdenticalOutput()
    {
        var schema = Schema(Table("Orders"), Table("Customers"));

        SchemaSnapshotSerializer.Serialize(schema)
            .Should().Be(SchemaSnapshotSerializer.Serialize(schema));
    }

    [Fact]
    public void Serialize_UnsortedTables_AreOrderedByName()
    {
        var json = SchemaSnapshotSerializer.Serialize(Schema(Table("Zebra"), Table("Apple")));

        json.IndexOf("Apple", StringComparison.Ordinal)
            .Should().BeLessThan(json.IndexOf("Zebra", StringComparison.Ordinal));
    }

    [Fact]
    public void Serialize_TableOrderDoesNotAffectOutput()
    {
        var a = SchemaSnapshotSerializer.Serialize(Schema(Table("Zebra"), Table("Apple")));
        var b = SchemaSnapshotSerializer.Serialize(Schema(Table("Apple"), Table("Zebra")));

        a.Should().Be(b, "the snapshot must be independent of the order tables are declared in");
    }

    [Fact]
    public void Serialize_UnsortedIndexes_AreOrderedByName()
    {
        var indexes = ImmutableArray.Create(
            new IndexSchema("IX_Zebra", ImmutableArray.Create("Id")),
            new IndexSchema("IX_Apple", ImmutableArray.Create("Id")));
        var json = SchemaSnapshotSerializer.Serialize(Schema(Table("Orders", indexes)));

        json.IndexOf("IX_Apple", StringComparison.Ordinal)
            .Should().BeLessThan(json.IndexOf("IX_Zebra", StringComparison.Ordinal));
    }

    [Fact]
    public void Serialize_UsesLfOnly_AndTrailingNewline()
    {
        var json = SchemaSnapshotSerializer.Serialize(Schema(Table("Orders")));

        json.Should().NotContain("\r");
        json.Should().EndWith("\n");
    }
}
