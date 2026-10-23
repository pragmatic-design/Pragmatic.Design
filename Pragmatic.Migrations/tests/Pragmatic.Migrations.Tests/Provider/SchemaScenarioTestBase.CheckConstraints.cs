#pragma warning disable CA2007 // ConfigureAwait in test code

using System.Collections.Immutable;
using System.Data.Common;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Tests.Provider;

public abstract partial class SchemaScenarioTestBase
{
    // ===========================================================================================
    // Scenarios 22-24: check constraints are created, read back, added, dropped — and converge
    // ===========================================================================================

    /// <summary>
    ///     A check, applied once, is up to date: the database reports it back, so the next diff finds
    ///     nothing and the next run applies nothing.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The diff compares checks by name. An introspector that reported none made every declared
    ///     check look missing on every run: SQLite proposed it forever (and its CREATE never carried it),
    ///     and on SQL Server the second run re-issued ADD CONSTRAINT and failed on the name, so a schema
    ///     with a check could not be migrated twice.
    /// </remarks>
    [Fact]
    public virtual async Task Scenario22_CheckConstraint_ConvergesAfterOneApply()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(Stock(withCheck: true));

        (await ApplySchemaAsync(conn, schema)).Success.Should().BeTrue();

        var current = await CreateIntrospector().IntrospectAsync(conn);
        current.Tables.First(t => t.Name == "Stock").CheckConstraints
            .Should().Contain(c => c.Name == "CK_Stock_Quantity", "the database holds the check that was applied");
        _diffEngine.ComputeDiff(schema, current).Changes.Select(change => change.ToString()).Should().BeEmpty(
            "the database holds exactly what was applied");

        var again = await ApplySchemaAsync(conn, schema);
        again.Success.Should().BeTrue(again.Error);
        again.ChangesApplied.Should().Be(0, "an up-to-date schema has nothing to apply");
    }

    /// <summary>A check added to a table that has rows: applied, enforced, the rows kept, and converged.</summary>
    [Fact]
    public virtual async Task Scenario23_CheckConstraint_AddedToAnExistingTable_IsEnforced()
    {
        await using var conn = await CreateConnectionAsync();
        (await ApplySchemaAsync(conn, MakeSchema(Stock(withCheck: false)))).Success.Should().BeTrue();
        await InsertStockAsync(conn, 1, 5);

        var withCheck = MakeSchema(Stock(withCheck: true));
        var result = await ApplySchemaAsync(conn, withCheck);
        result.Success.Should().BeTrue(result.Error);

        (await ScalarAsync(conn, """SELECT "Quantity" FROM "Stock" """)).Should().Be("5", "adding a check keeps the rows it admits");
        var violate = () => InsertStockAsync(conn, 2, -1);
        await violate.Should().ThrowAsync<DbException>();
        _diffEngine.ComputeDiff(withCheck, await CreateIntrospector().IntrospectAsync(conn)).Changes
            .Select(change => change.ToString()).Should().BeEmpty("a check reported as applied must not reappear on the next diff");
    }

    /// <summary>A check dropped from the schema is dropped from the database, and the diff converges.</summary>
    [Fact]
    public virtual async Task Scenario24_CheckConstraint_Dropped_Converges()
    {
        await using var conn = await CreateConnectionAsync();
        (await ApplySchemaAsync(conn, MakeSchema(Stock(withCheck: true)))).Success.Should().BeTrue();

        var withoutCheck = MakeSchema(Stock(withCheck: false));
        var result = await ApplySchemaAsync(conn, withoutCheck);
        result.Success.Should().BeTrue(result.Error);

        await InsertStockAsync(conn, 1, -1); // admitted now
        _diffEngine.ComputeDiff(withoutCheck, await CreateIntrospector().IntrospectAsync(conn)).Changes
            .Select(change => change.ToString()).Should().BeEmpty("a dropped check must not come back on the next diff");
    }

    private TableSchema Stock(bool withCheck) => new("Stock", null,
        ImmutableArray.Create(
            new ColumnSchema("Id", PkType(), false, true),
            new ColumnSchema("Quantity", IntType(), false, false)),
        [], [],
        withCheck ? [new CheckConstraintSchema("CK_Stock_Quantity", "\"Quantity\" >= 0")] : []);

    private static Task InsertStockAsync(DbConnection conn, int row, int quantity)
        => ExecuteAsync(conn, $"""INSERT INTO "Stock" ("Id", "Quantity") VALUES ('0195b0d6-0000-7000-8000-00000000000{row}', {quantity})""");
}
