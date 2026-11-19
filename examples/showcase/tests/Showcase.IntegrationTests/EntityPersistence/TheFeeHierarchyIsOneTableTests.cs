using Pragmatic.Testing.Assertions;
using Npgsql;
using Pragmatic.Migrations.Introspection;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     The Fee hierarchy ([Inheritance(Tph, DiscriminatorColumn = "FeeType")]) in the migrated PostgreSQL schema. The rows
///     written through it are in InheritanceTests.
/// </summary>
public class TheFeeHierarchyIsOneTableTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    ///     The hierarchy is one table in the migrated schema, and it holds the derived columns.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <c>ServiceFee</c> and <c>CancellationFee</c> now carry <c>[Entity]</c> — the form
    ///         <c>[Inheritance]</c>'s own documentation shows, and the only form that gives a derived
    ///         type a repository, mutations and permissions of its own. Until today it compiled and
    ///         killed the boundary at first use: the generated configuration claimed <c>ToTable</c> and
    ///         <c>HasKey</c> for a derived type, which EF refuses while building the model, and the
    ///         migration schema counted the derived types as tables on top of merging their columns into
    ///         the root's — three tables where TPH wants one.
    ///     </para>
    ///     <para>
    ///         Read from the live database through the introspector, so it says what the migration
    ///         created rather than what the generator meant. InheritanceTests is the other half of the
    ///         proof: it writes rows through the store and reads them back.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheHierarchy_IsOneTableInTheMigratedSchema()
    {
        await using var connection = new NpgsqlConnection(Fixture.FinancialConnectionString);
        await connection.OpenAsync();

        var schema = await new PostgreSqlSchemaIntrospector().IntrospectAsync(connection);
        var tables = schema.Tables.Select(t => t.Name).ToList();

        tables.Should().Contain("Fees", "the root's table is the hierarchy's table");
        tables.Should().NotContain("ServiceFees", "TPH is one table");
        tables.Should().NotContain("CancellationFeses", "and one table for every derived type");

        var fees = schema.Tables.First(t => t.Name == "Fees");
        var columns = fees.Columns.Select(c => c.Name).ToList();
        columns.Should().Contain("FeeType", "the discriminator [Inheritance] named");
        columns.Should().Contain("ServiceName", "ServiceFee's own column, merged into the root's table");
        columns.Should().Contain("PenaltyRate", "and CancellationFee's");
    }
}
