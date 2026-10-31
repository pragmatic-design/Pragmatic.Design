#pragma warning disable CA2007 // ConfigureAwait in test code

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Tests.Provider;

public abstract partial class SchemaScenarioTestBase
{
    // ===========================================================================================
    // Scenario 27: a unique index on a nullable binary of a fixed length — created, converged, many nulls
    // ===========================================================================================

    /// <summary>The provider's type for a binary of <paramref name="length" /> bytes that an index can hold.</summary>
    protected abstract string IndexableBinaryType(int length);

    /// <summary>
    ///     The filter a unique index over a nullable column needs for its rows without a value not to collide: SQL Server counts a null as a
    ///     value in a unique index; the other providers do not, and need none.
    /// </summary>
    protected virtual string? UniqueOverNullableFilter(string column) => null;

    /// <summary>
    ///     The subject registry's shape: a blind index (a 32-byte hash) unique among the subjects that have one, null once the subject is
    ///     forgotten. Created, read back as declared, and holding any number of forgotten rows.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A binary of no length (<c>varbinary(max)</c>, <c>BLOB</c>) is no index key on SQL Server, MySQL or Oracle, so the index, and the
    ///     migration with it, fails there.
    /// </remarks>
    [Fact]
    public virtual async Task Scenario27_AUniqueBinaryThatMayBeNull_IsCreatedConvergesAndHoldsManyNulls()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(new TableSchema("Lookups", null,
            ImmutableArray.Create(
                new ColumnSchema("Id", PkType(), false, true),
                new ColumnSchema("LookupIndex", IndexableBinaryType(32), true, false)),
            ImmutableArray.Create(new IndexSchema("IX_Lookups_LookupIndex", ImmutableArray.Create("LookupIndex"), true,
                UniqueOverNullableFilter("LookupIndex"))),
            []));

        var first = await ApplySchemaAsync(conn, schema);
        first.Success.Should().BeTrue(first.Error);

        _diffEngine.ComputeDiff(schema, await CreateIntrospector().IntrospectAsync(conn)).Changes.Select(change => change.ToString())
            .Should().BeEmpty("the database holds the column and the index the schema declared");

        await ExecuteAsync(conn, """INSERT INTO "Lookups" ("Id") VALUES ('0195b0d6-0000-7000-8000-000000000001')""");
        await ExecuteAsync(conn, """INSERT INTO "Lookups" ("Id") VALUES ('0195b0d6-0000-7000-8000-000000000002')""");
        (await ScalarAsync(conn, """SELECT COUNT(*) FROM "Lookups" """)).Should().Be("2", "two rows without a value do not collide");
    }
}
