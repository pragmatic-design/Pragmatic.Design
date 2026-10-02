#pragma warning disable CA2007 // ConfigureAwait in test code
#pragma warning disable CA1822 // MakeSchema can be static

using System.Collections.Immutable;
using System.Data.Common;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Configuration;
// NonOwningConnection lives with the hermetic suite and is linked into this project.
using Pragmatic.Migrations.Core.Tests.Provider;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Tests.Provider;

/// <summary>
///     Abstract base with schema migration scenarios executed on real databases.
///     Each provider subclass provides its own connection, generator, and introspector.
/// </summary>
public abstract partial class SchemaScenarioTestBase
{
    protected abstract ISqlMigrationGenerator CreateGenerator();
    protected abstract ISchemaIntrospector CreateIntrospector();
    protected abstract Task<DbConnection> CreateConnectionAsync();

    private readonly ISchemaDiffEngine _diffEngine = new SchemaDiffEngine();

    /// <summary>
    ///     Applies the desired schema through the REAL <see cref="MigrationRunner" /> against a real
    ///     database. The scenarios must exercise the production path: a helper that re-implements
    ///     execution can diverge from the runner — a SQLite branch the runner lacks can hide SQLite
    ///     migrations that report success without changing anything. Anything the runner cannot do,
    ///     these tests must not be able to do either.
    /// </summary>
    private async Task<MigrationResult> ApplySchemaAsync(DbConnection conn, SchemaVersion desired, bool force = false)
    {
        var generator = CreateGenerator();

        var services = new ServiceCollection();
        var builder = new MigrationsBuilder(services);
        // Hand the runner a non-owning view of the test's connection: same database, same session,
        // but disposing it does not tear down the connection the assertions run on.
        builder.UseProvider(generator.ProviderName, _ => new NonOwningConnection(conn));
        if (force) builder.Force();
        builder.Build();

        var provider = services.BuildServiceProvider();
        await using (provider)
        {
            var runner = provider.GetRequiredService<IMigrationRunner>();
            var options = provider.GetRequiredService<MigrationOptions>();
            var context = new MigrationContext(
                conn.ConnectionString,
                desired with { ProviderName = generator.ProviderName },
                options);

            return await runner.MigrateAsync(context);
        }
    }

    private SchemaVersion MakeSchema(params TableSchema[] tables)
        => new(ImmutableArray.Create(tables));

    /// <summary>
    ///     Every scenario starts against a database nothing has written to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The invariant the other nineteen rest on. With a container per test, an empty database
    ///         would be a consequence rather than a claim; the container is shared by the class and only
    ///         the database is per test, which makes this worth asserting rather than assuming.
    ///     </para>
    ///     <para>
    ///         ⚠️ It is not tidiness. <c>ApplySchemaAsync</c> runs the real <c>MigrationRunner</c>,
    ///         which diffs the desired schema against the <b>actual</b> database — so a leftover table
    ///         from another scenario does not merely sit there, it enters the diff and the runner
    ///         proposes dropping it. Sharing one database would not fail loudly; it would quietly
    ///         change what every scenario measures.
    ///     </para>
    /// </remarks>
    [Fact]
    public virtual async Task TheDatabase_StartsEmpty()
    {
        await using var conn = await CreateConnectionAsync();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);

        introspected.Tables.Should().BeEmpty(
            "each scenario diffs against the real database, so it needs one nobody else has written to");
    }

    // ===========================================================================================
    // Scenario 1: Create simple table
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario01_CreateSimpleTable()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("Users", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Name", TextType(), false, false)),
                [], []));

        var result = await ApplySchemaAsync(conn, schema);
        result.Success.Should().BeTrue();
        result.ChangesApplied.Should().BeGreaterThan(0);

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        introspected.Tables.Should().Contain(t => t.Name == "Users");
        var users = introspected.Tables.First(t => t.Name == "Users");
        users.Columns.Should().Contain(c => c.Name == "Id" && c.IsPrimaryKey);
        users.Columns.Should().Contain(c => c.Name == "Name" && !c.IsNullable);
    }

    // ===========================================================================================
    // Scenario 2: Create table with multiple column types
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario02_AllColumnTypes()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("TypeTests", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Name", TextType(), false, false),
                    new ColumnSchema("Age", IntType(), false, false),
                    new ColumnSchema("Score", DecimalType(), true, false),
                    new ColumnSchema("IsActive", BoolType(), false, false),
                    new ColumnSchema("CreatedAt", TimestampType(), false, false)),
                [], []));

        var result = await ApplySchemaAsync(conn, schema);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        var table = introspected.Tables.First(t => t.Name == "TypeTests");
        table.Columns.Should().HaveCount(6);
    }

    // ===========================================================================================
    // Scenario 3: Create table with unique index
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario03_UniqueIndex()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("Products", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Sku", IndexableTextType(), false, false)),
                ImmutableArray.Create(
                    new IndexSchema("IX_Products_Sku", ImmutableArray.Create("Sku"), true)),
                []));

        var result = await ApplySchemaAsync(conn, schema);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        var table = introspected.Tables.First(t => t.Name == "Products");
        table.Indexes.Should().Contain(i => i.Name == "IX_Products_Sku" && i.IsUnique);
    }

    // ===========================================================================================
    // Scenario 4: Create table with FK
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario04_ForeignKey()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("Categories", null,
                ImmutableArray.Create(new ColumnSchema("Id", PkType(), false, true)),
                [], []),
            new TableSchema("Items", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("CategoryId", PkType(), false, false)),
                ImmutableArray.Create(
                    new IndexSchema("IX_Items_CategoryId", ImmutableArray.Create("CategoryId"))),
                ImmutableArray.Create(
                    new ForeignKeySchema("FK_Items_CategoryId_Categories", "CategoryId", "Categories", "Id", ReferentialAction.Cascade))));

        var result = await ApplySchemaAsync(conn, schema);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        var items = introspected.Tables.First(t => t.Name == "Items");
        items.ForeignKeys.Should().Contain(fk => fk.ReferencedTable == "Categories");
    }

    // ===========================================================================================
    // Scenario 5: Add nullable column
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario05_AddNullableColumn()
    {
        await using var conn = await CreateConnectionAsync();

        // Step 1: Create table
        var v1 = MakeSchema(
            new TableSchema("Contacts", null,
                ImmutableArray.Create(new ColumnSchema("Id", PkType(), false, true)),
                [], []));
        await ApplySchemaAsync(conn, v1);

        // Step 2: Add column
        var v2 = MakeSchema(
            new TableSchema("Contacts", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Phone", TextType(), true, false)),
                [], []));
        var result = await ApplySchemaAsync(conn, v2);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        var table = introspected.Tables.First(t => t.Name == "Contacts");
        table.Columns.Should().Contain(c => c.Name == "Phone" && c.IsNullable);
    }

    // ===========================================================================================
    // Scenario 6: Add NOT NULL column with default
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario06_AddNotNullColumnWithDefault()
    {
        await using var conn = await CreateConnectionAsync();

        var v1 = MakeSchema(
            new TableSchema("Settings", null,
                ImmutableArray.Create(new ColumnSchema("Id", PkType(), false, true)),
                [], []));
        await ApplySchemaAsync(conn, v1);

        var v2 = MakeSchema(
            new TableSchema("Settings", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("IsEnabled", BoolType(), false, false, DefaultFalse())),
                [], []));

        var result = await ApplySchemaAsync(conn, v2);
        result.Success.Should().BeTrue();
    }

    // ===========================================================================================
    // Scenario 7: Drop column (breaking change)
    // ===========================================================================================
    [Fact]
    public virtual Task Scenario07_DropColumn_IsBreaking()
    {
        var generator = CreateGenerator();
        var diff = new SchemaDiff(
            ImmutableArray.Create<SchemaChange>(new DropColumn("Users", "OldField")), true);

        var sql = generator.GenerateScript(diff);
        sql.Should().NotBeEmpty();
        diff.HasBreakingChanges.Should().BeTrue();

        return Task.CompletedTask;
    }

    // ===========================================================================================
    // Scenario 8: Rename column
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario08_RenameColumn()
    {
        await using var conn = await CreateConnectionAsync();

        var v1 = MakeSchema(
            new TableSchema("People", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("FirstName", TextType(), false, false)),
                [], []));
        await ApplySchemaAsync(conn, v1);

        // A GUID written as a string literal: uuid, uniqueidentifier and TEXT all accept it, so one
        // statement serves every provider without a quoting helper per dialect.
        await ExecuteAsync(
            conn,
            """INSERT INTO "People" ("Id", "FirstName") """
            + """VALUES ('0195b0d6-0000-7000-8000-000000000001', 'Ada')""");

        var v2 = MakeSchema(
            new TableSchema("People", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("GivenName", TextType(), false, false, RenamedFrom: "FirstName")),
                [], []));

        var result = await ApplySchemaAsync(conn, v2);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        var table = introspected.Tables.First(t => t.Name == "People");
        table.Columns.Should().Contain(c => c.Name == "GivenName");
        table.Columns.Should().NotContain(c => c.Name == "FirstName");

        // The assertions above cannot tell a rename from a drop-and-add: both leave GivenName present
        // and FirstName gone. What [RenamedFrom] promises is the row, so the row is what this reads.
        (await ScalarAsync(conn, """SELECT "GivenName" FROM "People" """))
            .Should().Be("Ada",
                "a rename keeps the value; dropping the column and adding another loses it");
    }

    // ===========================================================================================
    // Scenario 9: Change nullability
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario09_ChangeNullability()
    {
        await using var conn = await CreateConnectionAsync();

        var v1 = MakeSchema(
            new TableSchema("Orders", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Note", TextType(), false, false)),
                [], []));
        await ApplySchemaAsync(conn, v1);

        // Make Note nullable
        var v2 = MakeSchema(
            new TableSchema("Orders", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Note", TextType(), true, false)),
                [], []));

        var result = await ApplySchemaAsync(conn, v2);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        var table = introspected.Tables.First(t => t.Name == "Orders");
        table.Columns.First(c => c.Name == "Note").IsNullable.Should().BeTrue();
    }

    // ===========================================================================================
    // Scenario 10: Add/Drop index lifecycle
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario10_IndexLifecycle()
    {
        await using var conn = await CreateConnectionAsync();

        var v1 = MakeSchema(
            new TableSchema("Logs", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Level", IndexableTextType(), false, false)),
                ImmutableArray.Create(
                    new IndexSchema("IX_Logs_Level", ImmutableArray.Create("Level"))),
                []));
        await ApplySchemaAsync(conn, v1);

        // Remove the index
        var v2 = MakeSchema(
            new TableSchema("Logs", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Level", TextType(), false, false)),
                [], []));

        var result = await ApplySchemaAsync(conn, v2);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        var table = introspected.Tables.First(t => t.Name == "Logs");
        table.Indexes.Should().NotContain(i => i.Name == "IX_Logs_Level");
    }

    // ===========================================================================================
    // Scenario 11: Soft-delete trait columns + filtered index
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario11_SoftDeleteTraitColumns()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("Documents", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Title", TextType(), false, false),
                    new ColumnSchema("IsDeleted", BoolType(), false, false, DefaultFalse()),
                    new ColumnSchema("DeletedAt", TimestampType(), true, false)),
                ImmutableArray.Create(
                    new IndexSchema("IX_Documents_IsDeleted", ImmutableArray.Create("IsDeleted"))),
                []));

        var result = await ApplySchemaAsync(conn, schema);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        var table = introspected.Tables.First(t => t.Name == "Documents");
        table.Columns.Should().Contain(c => c.Name == "IsDeleted" && !c.IsNullable);
        table.Columns.Should().Contain(c => c.Name == "DeletedAt" && c.IsNullable);
    }

    // ===========================================================================================
    // Scenario 12: Auditable trait columns
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario12_AuditableTraitColumns()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("Events", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("CreatedAt", TimestampType(), false, false),
                    new ColumnSchema("CreatedBy", TextType(), true, false),
                    new ColumnSchema("UpdatedAt", TimestampType(), true, false),
                    new ColumnSchema("UpdatedBy", TextType(), true, false)),
                [], []));

        var result = await ApplySchemaAsync(conn, schema);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        var table = introspected.Tables.First(t => t.Name == "Events");
        table.Columns.Should().HaveCount(5);
    }

    // ===========================================================================================
    // Scenario 13: Self-referencing FK
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario13_SelfReferencingFk()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("TreeNodes", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("ParentId", PkType(), true, false)),
                [],
                ImmutableArray.Create(
                    new ForeignKeySchema("FK_TreeNodes_ParentId_TreeNodes", "ParentId", "TreeNodes", "Id", ReferentialAction.Restrict))));

        var result = await ApplySchemaAsync(conn, schema);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        var table = introspected.Tables.First(t => t.Name == "TreeNodes");
        table.ForeignKeys.Should().Contain(fk => fk.ReferencedTable == "TreeNodes");
    }

    // ===========================================================================================
    // Scenario 14: Idempotent re-run (apply same schema 2x → 0 changes)
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario14_IdempotentRerun()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("IdempotentTest", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Value", TextType(), true, false)),
                [], []));

        await ApplySchemaAsync(conn, schema);

        // Second run — should detect no changes
        var introspector = CreateIntrospector();
        var current = await introspector.IntrospectAsync(conn);
        var diff = _diffEngine.ComputeDiff(schema, current);
        diff.HasChanges.Should().BeFalse();
    }

    // ===========================================================================================
    // Scenario 15: Empty DB → full schema (multiple tables + FK ordering)
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario15_EmptyDbToFullSchema()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(
            new TableSchema("Departments", null,
                ImmutableArray.Create(new ColumnSchema("Id", PkType(), false, true)),
                [], []),
            new TableSchema("Employees", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Name", TextType(), false, false),
                    new ColumnSchema("DeptId", PkType(), false, false)),
                ImmutableArray.Create(
                    new IndexSchema("IX_Employees_DeptId", ImmutableArray.Create("DeptId"))),
                ImmutableArray.Create(
                    new ForeignKeySchema("FK_Employees_DeptId_Departments", "DeptId", "Departments", "Id", ReferentialAction.Cascade))));

        var result = await ApplySchemaAsync(conn, schema);
        result.Success.Should().BeTrue();

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        introspected.Tables.Should().HaveCount(2);
    }

    // ===========================================================================================
    // Scenario 16: Breaking changes blocked without Force
    // ===========================================================================================
    [Fact]
    public void Scenario16_BreakingChangesDetection()
    {
        var changes = ImmutableArray.Create<SchemaChange>(
            new DropColumn("Users", "Email"),
            new AlterColumnNullability("Users", "Name", false));

        var diff = new SchemaDiff(changes, true);
        diff.HasBreakingChanges.Should().BeTrue();
        diff.Changes.Count(c => c.IsBreaking).Should().Be(2);
    }

    /// <summary>
    ///     A change that loses data is refused, and the column is still there afterwards.
    /// </summary>
    /// <remarks>
    ///     The scenario above is named for a block and asserts a boolean on a diff: it never runs the
    ///     migration, so it cannot say whether anything is blocked. This runs it. The column check is
    ///     the part that matters — a failed result with the column already dropped would be a worse
    ///     outcome than either success or refusal.
    /// </remarks>
    [Fact]
    public virtual async Task Scenario16b_ADroppedColumn_IsRefusedWithoutForce()
    {
        await using var conn = await CreateConnectionAsync();

        var withEmail = MakeSchema(
            new TableSchema("Members", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Email", TextType(), false, false)),
                [], []));
        await ApplySchemaAsync(conn, withEmail);

        var withoutEmail = MakeSchema(
            new TableSchema("Members", null,
                ImmutableArray.Create(new ColumnSchema("Id", PkType(), false, true)),
                [], []));

        var refused = await ApplySchemaAsync(conn, withoutEmail);

        refused.Success.Should().BeFalse("dropping a column loses what was in it");
        refused.Error.Should().Contain("Force",
            "the refusal has to say how to proceed, or it is a wall rather than a guard");

        var afterRefusal = await CreateIntrospector().IntrospectAsync(conn);
        afterRefusal.Tables.First(x => x.Name == "Members").Columns
            .Should().Contain(c => c.Name == "Email",
                "refused means nothing happened, not that some of it happened");
    }

    /// <summary>
    ///     And Force applies it, which is what makes the refusal a decision rather than a limit.
    /// </summary>
    /// <remarks>
    ///     The control for the test above: without it, a runner that refused every migration would
    ///     also pass.
    /// </remarks>
    [Fact]
    public virtual async Task Scenario16c_TheSameChange_IsAppliedWithForce()
    {
        await using var conn = await CreateConnectionAsync();

        var withPhone = MakeSchema(
            new TableSchema("Contacts", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Phone", TextType(), false, false)),
                [], []));
        await ApplySchemaAsync(conn, withPhone);

        var withoutPhone = MakeSchema(
            new TableSchema("Contacts", null,
                ImmutableArray.Create(new ColumnSchema("Id", PkType(), false, true)),
                [], []));

        var forced = await ApplySchemaAsync(conn, withoutPhone, force: true);

        forced.Success.Should().BeTrue(forced.Error);

        var after = await CreateIntrospector().IntrospectAsync(conn);
        after.Tables.First(x => x.Name == "Contacts").Columns
            .Should().NotContain(c => c.Name == "Phone");
    }

    // ===========================================================================================
    // Scenario 17: Change a column default on an EXISTING table
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario17_ChangeColumnDefault()
    {
        await using var conn = await CreateConnectionAsync();

        var v1 = MakeSchema(
            new TableSchema("Flags", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Enabled", BoolType(), false, false, DefaultFalse())),
                [], []));
        await ApplySchemaAsync(conn, v1);

        var v2 = MakeSchema(
            new TableSchema("Flags", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("Enabled", BoolType(), false, false, DefaultTrue())),
                [], []));

        var result = await ApplySchemaAsync(conn, v2);
        result.Success.Should().BeTrue(result.Error);

        // The change must actually be gone from the diff, not merely reported as applied.
        var current = await CreateIntrospector().IntrospectAsync(conn);
        _diffEngine.ComputeDiff(v2, current).HasChanges
            .Should().BeFalse("a change reported as applied must not reappear on the next diff");
    }

    // ===========================================================================================
    // Scenario 18: Add a FK to an EXISTING table
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario18_AddForeignKeyToExistingTable()
    {
        await using var conn = await CreateConnectionAsync();

        var v1 = MakeSchema(
            new TableSchema("Owners", null,
                ImmutableArray.Create(new ColumnSchema("Id", PkType(), false, true)),
                [], []),
            new TableSchema("Pets", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("OwnerId", PkType(), false, false)),
                [], []));
        await ApplySchemaAsync(conn, v1);

        var v2 = MakeSchema(
            new TableSchema("Owners", null,
                ImmutableArray.Create(new ColumnSchema("Id", PkType(), false, true)),
                [], []),
            new TableSchema("Pets", null,
                ImmutableArray.Create(
                    new ColumnSchema("Id", PkType(), false, true),
                    new ColumnSchema("OwnerId", PkType(), false, false)),
                [],
                ImmutableArray.Create(
                    new ForeignKeySchema("FK_Pets_OwnerId_Owners", "OwnerId", "Owners", "Id", ReferentialAction.Cascade))));

        var result = await ApplySchemaAsync(conn, v2);
        result.Success.Should().BeTrue(result.Error);

        var introspected = await CreateIntrospector().IntrospectAsync(conn);
        introspected.Tables.First(t => t.Name == "Pets").ForeignKeys
            .Should().Contain(fk => fk.ReferencedTable == "Owners");
    }

    // ===========================================================================================
    // Scenario 19: introspection cost does not grow with the number of tables
    // ===========================================================================================
    [Fact]
    public virtual async Task Scenario19_IntrospectionCostIsConstant()
    {
        await using var conn = await CreateConnectionAsync();

        // Two schemas of very different size, both introspected on the same connection.
        await ApplySchemaAsync(conn, MakeSchema(NumberedTables(1)));
        var counting = new CountingConnection(conn);

        counting.Reset();
        var small = await CreateIntrospector().IntrospectAsync(counting);
        var costForOne = counting.Executions;

        await ApplySchemaAsync(conn, MakeSchema(NumberedTables(8)));

        counting.Reset();
        var large = await CreateIntrospector().IntrospectAsync(counting);
        var costForEight = counting.Executions;

        small.Tables.Should().HaveCount(1);
        large.Tables.Should().HaveCount(8);

        // A per-table query would make this grow with the schema — three round-trips per table was
        // the old shape, so eight tables cost 25 instead of a handful.
        costForEight.Should().Be(costForOne,
            "introspection must cost the same number of round-trips regardless of table count");
        costForOne.Should().BeLessThan(8, "a fixed handful of bulk queries, not one per table");
    }

    private TableSchema[] NumberedTables(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new TableSchema($"Bulk{i}", null,
            ImmutableArray.Create(
                new ColumnSchema("Id", PkType(), false, true),
                new ColumnSchema("Label", IndexableTextType(), true, false)),
            ImmutableArray.Create(new IndexSchema($"IX_Bulk{i}_Label", ImmutableArray.Create("Label"))),
            []))];

    // ===========================================================================================
    // Provider-specific type helpers (override in subclass)
    // ===========================================================================================
    /// <summary>Runs one statement on the test connection.</summary>
    private static async Task ExecuteAsync(DbConnection conn, string sql)
    {
        await using var command = conn.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Reads one value back as a string, whatever the provider stored it as.</summary>
    private static async Task<string?> ScalarAsync(DbConnection conn, string sql)
    {
        await using var command = conn.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();

        return value is null || value is System.DBNull ? null : value.ToString();
    }

    protected abstract string PkType();
    protected abstract string TextType();
    /// <summary>Text type that can be used in indexes (SQL Server limits nvarchar(max) in index keys).</summary>
    protected virtual string IndexableTextType() => TextType();
    protected abstract string IntType();
    protected abstract string BoolType();
    protected abstract string DecimalType();
    protected abstract string TimestampType();
    protected abstract string DefaultFalse();

    /// <summary>Literal for a boolean "true" default, in the provider's own spelling.</summary>
    protected abstract string DefaultTrue();
}
