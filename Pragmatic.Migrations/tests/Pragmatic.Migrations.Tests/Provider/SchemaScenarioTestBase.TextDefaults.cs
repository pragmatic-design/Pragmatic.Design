using System.Collections.Immutable;
using Pragmatic.Migrations.Schema;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Migrations.Tests.Provider;

public abstract partial class SchemaScenarioTestBase
{
    // ===========================================================================================
    // Scenario 28: a text default converges after one apply
    // ===========================================================================================

    /// <summary>
    ///     A column with a text default, migrated once, has nothing left to apply.
    /// </summary>
    /// <remarks>
    ///     PostgreSQL reports the default with its cast (<c>'IT'::text</c>) where the desired schema writes <c>'IT'</c>. Compared as
    ///     reported, the two never matched: every start planned an <c>ALTER DEFAULT</c>, and a host that waited for the migration
    ///     leader was refused because the schema was "still behind".
    /// </remarks>
    [Fact]
    public virtual async Task Scenario28_TextDefault_ConvergesAfterOneApply()
    {
        var conn = await CreateConnectionAsync().ConfigureAwait(true);
        await using (conn.ConfigureAwait(true))
        {
            var schema = MakeSchema(
                new TableSchema("Organizations", null,
                    ImmutableArray.Create(
                        new ColumnSchema("Id", PkType(), false, true),
                        new ColumnSchema("Country", IndexableTextType(), false, false, "'IT'")),
                    [], []));

            (await ApplySchemaAsync(conn, schema).ConfigureAwait(true)).Success.Should().BeTrue();

            var current = await CreateIntrospector().IntrospectAsync(conn).ConfigureAwait(true);
            _diffEngine.ComputeDiff(schema, current).Changes.Select(change => change.ToString()).Should().BeEmpty(
                "the database holds the default that was applied");

            var again = await ApplySchemaAsync(conn, schema).ConfigureAwait(true);
            again.Success.Should().BeTrue(again.Error);
            again.ChangesApplied.Should().Be(0, "an up-to-date schema has nothing to apply");
        }
    }
}
