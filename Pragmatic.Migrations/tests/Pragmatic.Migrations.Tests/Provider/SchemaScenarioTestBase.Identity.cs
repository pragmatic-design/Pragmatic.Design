#pragma warning disable CA2007 // ConfigureAwait in test code

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Tests.Provider;

public abstract partial class SchemaScenarioTestBase
{
    // ===========================================================================================
    // Scenario 26: a key the database assigns — created, and converged
    // ===========================================================================================

    /// <summary>The provider's type for a 64-bit key the database assigns, as a schema declares it.</summary>
    protected abstract string IdentityKeyType();

    /// <summary>
    ///     A key the database assigns is created as the provider's identity, and the next run finds it there: the type the database reports
    ///     back (the plain integer) is the one the schema declared, so the diff is empty and nothing is applied again.
    /// </summary>
    /// <remarks>⚠️ The identity is a column property the database does not report as part of the type: read back literally, every run would change it.</remarks>
    [Fact]
    public virtual async Task Scenario26_AKeyTheDatabaseAssigns_IsCreatedAndConverges()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(new TableSchema("Ledger", null,
            ImmutableArray.Create(
                new ColumnSchema("Seq", IdentityKeyType(), false, true),
                new ColumnSchema("Amount", IntType(), false, false)),
            [], []));

        var first = await ApplySchemaAsync(conn, schema);
        first.Success.Should().BeTrue(first.Error);

        _diffEngine.ComputeDiff(schema, await CreateIntrospector().IntrospectAsync(conn)).Changes.Select(change => change.ToString())
            .Should().BeEmpty("the database holds the key the schema declared");

        var again = await ApplySchemaAsync(conn, schema);
        again.Success.Should().BeTrue(again.Error);
        again.ChangesApplied.Should().Be(0, "an up-to-date schema has nothing to apply");
    }
}
