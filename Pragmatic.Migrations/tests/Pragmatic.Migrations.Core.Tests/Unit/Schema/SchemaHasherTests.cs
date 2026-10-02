using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Core.Tests.Unit.Schema;

/// <summary>
///     The hash is only trustworthy if it covers exactly what the diff engine compares. Too little
///     and two schemas the diff calls different would share a hash — the runner's fast path would
///     then skip real changes. Too much and two identical schemas would differ, making the hash
///     useless for drift detection. Both directions are pinned here, one case per kind of change.
/// </summary>
public class SchemaHasherTests
{
    private static SchemaVersion Schema(params TableSchema[] tables) => new([.. tables]);

    private static TableSchema Orders(
        ImmutableArray<ColumnSchema>? columns = null,
        ImmutableArray<IndexSchema>? indexes = null,
        ImmutableArray<ForeignKeySchema>? foreignKeys = null,
        string? schemaName = "public") =>
        new("Orders", schemaName,
            columns ?? [
                new ColumnSchema("Id", "uuid", false, true),
                new ColumnSchema("Total", "numeric(18,2)", false, false, "0")
            ],
            indexes ?? [new IndexSchema("IX_Orders_Total", ["Total"])],
            foreignKeys ?? []);

    private static string HashOf(params TableSchema[] tables) => Schema(tables).Hash;

    // =====================================================================================
    // Sensitivity: every difference the diff engine reports must change the hash
    // =====================================================================================

    [Fact]
    public void Hash_ChangesWhen_TableIsAdded() =>
        HashOf(Orders()).Should().NotBe(
            HashOf(Orders(), new TableSchema("Extra", "public", [new ColumnSchema("Id", "uuid", false, true)], [], [])));

    [Fact]
    public void Hash_ChangesWhen_ColumnIsAdded() =>
        HashOf(Orders()).Should().NotBe(HashOf(Orders(columns: [
            new ColumnSchema("Id", "uuid", false, true),
            new ColumnSchema("Total", "numeric(18,2)", false, false, "0"),
            new ColumnSchema("Note", "text", true, false)
        ])));

    [Fact]
    public void Hash_ChangesWhen_ColumnTypeChanges() =>
        HashOf(Orders()).Should().NotBe(HashOf(Orders(columns: [
            new ColumnSchema("Id", "uuid", false, true),
            new ColumnSchema("Total", "numeric(18,4)", false, false, "0")
        ])));

    [Fact]
    public void Hash_ChangesWhen_NullabilityChanges() =>
        HashOf(Orders()).Should().NotBe(HashOf(Orders(columns: [
            new ColumnSchema("Id", "uuid", false, true),
            new ColumnSchema("Total", "numeric(18,2)", true, false, "0")
        ])));

    [Fact]
    public void Hash_ChangesWhen_DefaultChanges() =>
        HashOf(Orders()).Should().NotBe(HashOf(Orders(columns: [
            new ColumnSchema("Id", "uuid", false, true),
            new ColumnSchema("Total", "numeric(18,2)", false, false, "1")
        ])));

    [Fact]
    public void Hash_ChangesWhen_DefaultIsRemoved() =>
        HashOf(Orders()).Should().NotBe(HashOf(Orders(columns: [
            new ColumnSchema("Id", "uuid", false, true),
            new ColumnSchema("Total", "numeric(18,2)", false, false)
        ])));

    [Fact]
    public void Hash_ChangesWhen_PrimaryKeyChanges() =>
        HashOf(Orders()).Should().NotBe(HashOf(Orders(columns: [
            new ColumnSchema("Id", "uuid", false, true),
            new ColumnSchema("Total", "numeric(18,2)", false, true, "0")
        ])));

    [Fact]
    public void Hash_ChangesWhen_IndexIsAdded() =>
        HashOf(Orders()).Should().NotBe(HashOf(Orders(indexes: [
            new IndexSchema("IX_Orders_Total", ["Total"]),
            new IndexSchema("IX_Orders_Id", ["Id"])
        ])));

    [Fact]
    public void Hash_ChangesWhen_IndexBecomesUnique() =>
        HashOf(Orders()).Should().NotBe(HashOf(Orders(indexes: [
            new IndexSchema("IX_Orders_Total", ["Total"], IsUnique: true)
        ])));

    [Fact]
    public void Hash_ChangesWhen_IndexFilterChanges() =>
        HashOf(Orders()).Should().NotBe(HashOf(Orders(indexes: [
            new IndexSchema("IX_Orders_Total", ["Total"], Filter: "\"Total\" > 0")
        ])));

    [Fact]
    public void Hash_ChangesWhen_IndexColumnOrderChanges()
    {
        var ab = Orders(indexes: [new IndexSchema("IX", ["Id", "Total"])]);
        var ba = Orders(indexes: [new IndexSchema("IX", ["Total", "Id"])]);

        HashOf(ab).Should().NotBe(HashOf(ba), "a composite index is ordered");
    }

    [Fact]
    public void Hash_ChangesWhen_ForeignKeyIsAdded() =>
        HashOf(Orders()).Should().NotBe(HashOf(Orders(foreignKeys: [
            new ForeignKeySchema("FK", "Id", "Customers", "Id", ReferentialAction.NoAction)
        ])));

    [Fact]
    public void Hash_ChangesWhen_ForeignKeyDeleteBehaviourChanges()
    {
        var noAction = Orders(foreignKeys: [new ForeignKeySchema("FK", "Id", "Customers", "Id", ReferentialAction.NoAction)]);
        var cascade = Orders(foreignKeys: [new ForeignKeySchema("FK", "Id", "Customers", "Id", ReferentialAction.Cascade)]);

        HashOf(noAction).Should().NotBe(HashOf(cascade));
    }

    // =====================================================================================
    // Stability: differences the diff engine does NOT report must not change the hash
    // =====================================================================================

    [Fact]
    public void Hash_IsStableAcross_TypeSpellings()
    {
        // What the compile-time schema writes vs what PostgreSQL reports back. The diff normalises
        // these away, so the hash must too — this is the whole reason the two sides can be compared.
        var declared = Orders(columns: [
            new ColumnSchema("Id", "uuid", false, true),
            new ColumnSchema("Flag", "boolean", false, false),
            new ColumnSchema("Count", "integer", false, false),
            new ColumnSchema("Name", "character varying(50)", true, false)
        ]);
        var introspected = Orders(columns: [
            new ColumnSchema("Id", "uuid", false, true),
            new ColumnSchema("Flag", "bool", false, false),
            new ColumnSchema("Count", "int4", false, false),
            new ColumnSchema("Name", "varchar(50)", true, false)
        ]);

        HashOf(declared).Should().Be(HashOf(introspected));
    }

    [Fact]
    public void Hash_IsStableAcross_DefaultParenWrapping()
    {
        // SQL Server stores defaults as ((0)); the desired schema carries 0.
        var plain = Orders(columns: [new ColumnSchema("Id", "int", false, true, "0")]);
        var wrapped = Orders(columns: [new ColumnSchema("Id", "int", false, true, "((0))")]);

        HashOf(plain).Should().Be(HashOf(wrapped));
    }

    [Fact]
    public void Hash_IsStableAcross_SchemaQualifier()
    {
        // The generator usually leaves SchemaName null, introspection always fills it in — and the
        // diff treats null as "wherever the table actually lives".
        HashOf(Orders(schemaName: null)).Should().Be(HashOf(Orders(schemaName: "public")));
    }

    [Fact]
    public void Hash_IsStableAcross_TableAndColumnOrder()
    {
        var a = new TableSchema("A", null, [new ColumnSchema("X", "int", false, true), new ColumnSchema("Y", "int", true, false)], [], []);
        var aReordered = new TableSchema("A", null, [new ColumnSchema("Y", "int", true, false), new ColumnSchema("X", "int", false, true)], [], []);
        var b = new TableSchema("B", null, [new ColumnSchema("X", "int", false, true)], [], []);

        HashOf(a, b).Should().Be(HashOf(b, aReordered));
    }

    [Fact]
    public void Hash_IsStableAcross_IndexAndForeignKeyNames()
    {
        // Both are matched structurally by the diff — the name is not part of the identity.
        var named = Orders(
            indexes: [new IndexSchema("IX_ours", ["Total"])],
            foreignKeys: [new ForeignKeySchema("FK_ours", "Id", "Customers", "Id", ReferentialAction.Cascade)]);
        var renamed = Orders(
            indexes: [new IndexSchema("IX_theirs", ["Total"])],
            foreignKeys: [new ForeignKeySchema("FK_theirs", "Id", "Customers", "Id", ReferentialAction.Cascade)]);

        HashOf(named).Should().Be(HashOf(renamed));
    }

    [Fact]
    public void Hash_IsStableAcross_IdentifierCasing() =>
        HashOf(Orders()).Should().Be(HashOf(new TableSchema("ORDERS", "public",
            [new ColumnSchema("ID", "uuid", false, true), new ColumnSchema("TOTAL", "numeric(18,2)", false, false, "0")],
            [new IndexSchema("IX_Orders_Total", ["TOTAL"])], [])));

    [Fact]
    public void Hash_IgnoresMetadataThatIsNotSchema()
    {
        // Database name, provider and config key describe WHERE the schema lives, not what it is.
        var a = new SchemaVersion([Orders()], DatabaseName: "AppDb", ProviderName: "PostgreSql", ConfigKey: "ConnectionStrings:App");
        var b = new SchemaVersion([Orders()], DatabaseName: "OtherDb", ProviderName: "Sqlite", ConfigKey: "ConnectionStrings:Other");

        a.Hash.Should().Be(b.Hash);
    }

    // =====================================================================================
    // The property the fast path relies on
    // =====================================================================================

    [Fact]
    public void EqualHashes_MeanTheDiffEngineFindsNothing()
    {
        // This is the contract the runner's fast path depends on: skipping the diff on equal
        // hashes is only safe while this holds.
        var declared = Schema(Orders(schemaName: null, columns: [
            new ColumnSchema("Id", "uuid", false, true),
            new ColumnSchema("Flag", "boolean", false, false, "false")
        ]));
        var introspected = Schema(Orders(schemaName: "public", columns: [
            new ColumnSchema("Id", "uuid", false, true),
            new ColumnSchema("Flag", "bool", false, false, "false")
        ]));

        declared.Hash.Should().Be(introspected.Hash);
        new SchemaDiffEngine().ComputeDiff(declared, introspected).HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Hash_IsDeterministicAcrossInstances() =>
        Schema(Orders()).Hash.Should().Be(Schema(Orders()).Hash);

    [Fact]
    public void Hash_IsTwelveLowercaseHexCharacters() =>
        Schema(Orders()).Hash.Should().MatchRegex("^[0-9a-f]{12}$");
}
