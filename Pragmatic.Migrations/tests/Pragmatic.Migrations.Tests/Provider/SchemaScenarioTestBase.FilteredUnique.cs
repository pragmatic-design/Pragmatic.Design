#pragma warning disable CA2007 // ConfigureAwait in test code

using System.Collections.Immutable;
using System.Data.Common;
using Pragmatic.Migrations.Schema;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Migrations.Tests.Provider;

public abstract partial class SchemaScenarioTestBase
{
    // ===========================================================================================
    // Scenario 29: a filtered unique index, as the generator writes [Unique(Filter = …)] (#729) — the rows outside it are free
    // ===========================================================================================

    /// <summary>
    ///     The two predicates the generator writes for a provider, created on its database: one open stretch per desk (<c>"ValidTo" IS NULL</c>)
    ///     and any number of closed ones; a code unique among a soft-deleted entity's live, active rows. Two rows with the same key, one outside
    ///     the filter, are both written; two inside it are refused. Only where a partial index exists (PostgreSQL, SQL Server).
    /// </summary>
    /// <param name="activeFilter">The generator's text for <c>"Active" = {bt:1}</c> on a soft-deleted entity, on this provider.</param>
    /// <param name="trueLiteral">The provider's literal for true, in the inserts.</param>
    /// <param name="falseLiteral">The provider's literal for false.</param>
    private protected async Task AFilteredUnique_HoldsTheRowsItsFilterSelectsOnlyAsync(string activeFilter, string trueLiteral, string falseLiteral)
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("Stretches", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Desk", IndexableTextType(), false, false),
                    new ColumnSchema("ValidTo", TimestampType(), true, false)),
                ImmutableArray.Create(new IndexSchema("IX_Stretches_Desk", ImmutableArray.Create("Desk"), true, "\"ValidTo\" IS NULL")),
                []),
            new TableSchema("Vouchers", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Code", IndexableTextType(), false, false),
                    new ColumnSchema("IsDeleted", BoolType(), false, false),
                    new ColumnSchema("Active", BoolType(), false, false)),
                ImmutableArray.Create(new IndexSchema("IX_Vouchers_Code", ImmutableArray.Create("Code"), true, activeFilter)),
                []));

        var applied = await ApplySchemaAsync(conn, schema);
        applied.Success.Should().BeTrue(applied.Error);

        // A closed stretch and an open one of the same desk: the closed one is outside the filter.
        await ExecuteAsync(conn, """INSERT INTO "Stretches" ("Id", "Desk", "ValidTo") VALUES ('0195b0d6-0000-7000-8000-000000000101', 'A', '2026-01-01')""");
        await ExecuteAsync(conn, """INSERT INTO "Stretches" ("Id", "Desk", "ValidTo") VALUES ('0195b0d6-0000-7000-8000-000000000102', 'A', NULL)""");
        (await ScalarAsync(conn, """SELECT COUNT(*) FROM "Stretches" """)).Should().Be("2", "a row outside the filter does not collide");
        (await Record.ExceptionAsync(() => ExecuteAsync(conn, """INSERT INTO "Stretches" ("Id", "Desk", "ValidTo") VALUES ('0195b0d6-0000-7000-8000-000000000103', 'A', NULL)""")))
            .Should().BeAssignableTo<DbException>("two open stretches of one desk are both inside the filter");

        // Active and live; inactive; deleted: the last two are outside the filter.
        await ExecuteAsync(conn, $"""INSERT INTO "Vouchers" ("Id", "Code", "IsDeleted", "Active") VALUES ('0195b0d6-0000-7000-8000-000000000201', 'X', {falseLiteral}, {trueLiteral})""");
        await ExecuteAsync(conn, $"""INSERT INTO "Vouchers" ("Id", "Code", "IsDeleted", "Active") VALUES ('0195b0d6-0000-7000-8000-000000000202', 'X', {falseLiteral}, {falseLiteral})""");
        await ExecuteAsync(conn, $"""INSERT INTO "Vouchers" ("Id", "Code", "IsDeleted", "Active") VALUES ('0195b0d6-0000-7000-8000-000000000203', 'X', {trueLiteral}, {trueLiteral})""");
        (await Record.ExceptionAsync(() => ExecuteAsync(conn, $"""INSERT INTO "Vouchers" ("Id", "Code", "IsDeleted", "Active") VALUES ('0195b0d6-0000-7000-8000-000000000204', 'X', {falseLiteral}, {trueLiteral})""")))
            .Should().BeAssignableTo<DbException>("a second live, active voucher of the code is inside the filter");
    }
}
