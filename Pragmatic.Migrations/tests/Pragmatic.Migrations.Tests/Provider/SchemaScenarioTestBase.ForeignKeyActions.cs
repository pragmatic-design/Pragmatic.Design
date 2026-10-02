#pragma warning disable CA2007 // ConfigureAwait in test code

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Tests.Provider;

public abstract partial class SchemaScenarioTestBase
{
    // ===========================================================================================
    // Scenario 20: A schema with a foreign key of every delete action converges (apply 2x → 0 changes)
    // ===========================================================================================

    /// <summary>
    ///     Applied once, the schema is up to date: diffing it again against the database finds nothing,
    ///     whatever ON DELETE its foreign keys declare.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every generator writes <c>Restrict</c> as <c>NO ACTION</c> — SQL Server has no
    ///         <c>RESTRICT</c> — so the database reports <c>NO ACTION</c> back. Compared with the
    ///         <c>Restrict</c> the model declares, that would be a drop and an add for every such key, on
    ///         every start.
    ///     </para>
    ///     <para>
    ///         <c>Scenario14_IdempotentRerun</c> cannot see it: its table has no foreign key, so it never
    ///         reaches that comparison.
    ///     </para>
    /// </remarks>
    [Fact]
    public virtual async Task Scenario20_EveryDeleteAction_ConvergesAfterOneApply()
    {
        await using var conn = await CreateConnectionAsync();
        // A principal per key: SQL Server refuses two keys from one table to one table when either
        // cascades ("multiple cascade paths"), which is its rule and not what this measures.
        var schema = MakeSchema(
            Principal("NoActionOwners"),
            Principal("RestrictOwners"),
            Principal("CascadeOwners"),
            Principal("SetNullOwners"),
            new TableSchema("Holdings", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("NoActionOwnerId", PkType(), false, false),
                    new ColumnSchema("RestrictOwnerId", PkType(), false, false),
                    new ColumnSchema("CascadeOwnerId", PkType(), false, false),
                    new ColumnSchema("SetNullOwnerId", PkType(), true, false)),
                [],
                ImmutableArray.Create(
                    new ForeignKeySchema("FK_Holdings_NoActionOwnerId_NoActionOwners", "NoActionOwnerId", "NoActionOwners", "Id", ReferentialAction.NoAction),
                    new ForeignKeySchema("FK_Holdings_RestrictOwnerId_RestrictOwners", "RestrictOwnerId", "RestrictOwners", "Id", ReferentialAction.Restrict),
                    new ForeignKeySchema("FK_Holdings_CascadeOwnerId_CascadeOwners", "CascadeOwnerId", "CascadeOwners", "Id", ReferentialAction.Cascade),
                    new ForeignKeySchema("FK_Holdings_SetNullOwnerId_SetNullOwners", "SetNullOwnerId", "SetNullOwners", "Id", ReferentialAction.SetNull))));

        (await ApplySchemaAsync(conn, schema)).Success.Should().BeTrue();

        var current = await CreateIntrospector().IntrospectAsync(conn);
        var diff = _diffEngine.ComputeDiff(schema, current);

        diff.Changes.Select(change => change.ToString()).Should().BeEmpty(
            "the database holds exactly what was applied, so an up-to-date schema has nothing to change");
    }

    // ===========================================================================================
    // Scenario 21: A filtered index converges (apply 2x → 0 changes)
    // ===========================================================================================

    /// <summary>
    ///     A partial index, applied once, is up to date: the database's own spelling of the predicate is the
    ///     same predicate.
    /// </summary>
    /// <remarks>
    ///     PostgreSQL returns the predicate wrapped in parentheses (<c>("Status" = 0)</c>). Compared as a
    ///     string with the <c>"Status" = 0</c> the model declares, a filtered index would be dropped and
    ///     recreated on every start.
    /// </remarks>
    [Fact]
    public virtual async Task Scenario21_FilteredIndex_ConvergesAfterOneApply()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("Sagas", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("CorrelationId", PkType(), false, false),
                    new ColumnSchema("Status", IntType(), false, false)),
                ImmutableArray.Create(
                    new IndexSchema("IX_Sagas_CorrelationId", ImmutableArray.Create("CorrelationId"), true, FilterStatusIsZero())),
                []));

        (await ApplySchemaAsync(conn, schema)).Success.Should().BeTrue();

        var current = await CreateIntrospector().IntrospectAsync(conn);
        var diff = _diffEngine.ComputeDiff(schema, current);

        diff.Changes.Select(change => change.ToString()).Should().BeEmpty(
            "the database holds exactly the partial index that was applied");
    }

    /// <summary>The predicate as a model declares it for this provider: <c>Status = 0</c>, quoted its way.</summary>
    protected virtual string FilterStatusIsZero() => "\"Status\" = 0";

    private TableSchema Principal(string name)
        => new(name, null, ImmutableArray.Create(new ColumnSchema("Id", PkType(), false, true)), [], []);
}
