using Pragmatic.Migrations.Schema;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The schema the Sql engine describes for Pragmatic's entities (<c>DefaultSchema</c>) through Pragmatic.Migrations' runner, on the three
///     databases it migrates: created from nothing, converged (a second run finds nothing to do), evolved (a column and an index more, the rows
///     kept). And the two forms EF Core's branch used to write on SQL Server (an unbounded indexed string, a <c>false</c> default), each put
///     back alone: neither migrates, which is why both branches now write <c>nvarchar(450)</c> and <c>0</c> there.
/// </summary>
[Collection("pragmatic databases")]
public class PragmaticMigrationTests(PragmaticDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_generated_schema_is_created_from_nothing_and_converges(SqlDialect dialect)
    {
        var schema = DialectSchemas.For(dialect);
        var connectionString = await databases.CreateAsync(dialect, "created");

        var created = await PragmaticMigrations.MigrateAsync(connectionString, schema);
        Assert.True(created.Success, created.Error + "\n" + created.GeneratedSql);
        Assert.Equal(schema.Tables.Length, created.AppliedChanges.Count(c => c.GetType().Name == "CreateTable"));

        // The database introspected is the schema declared: nothing left to do.
        var diff = await PragmaticMigrations.DiffAsync(dialect, connectionString, schema);
        Assert.True(diff.IsEmpty, "left to do: " + string.Join("; ", diff.Select(c => $"{c.Description}{(c.IsBreaking ? " (breaking)" : "")} {c}")));
        var again = await PragmaticMigrations.MigrateAsync(connectionString, schema);
        Assert.True(again.Success, again.Error);
        Assert.Equal(0, again.ChangesApplied);
    }

    /// <summary>
    ///     The trail's entries, migrated, have the four indexes EF Core's configuration gives them: a segment in order, a subject over time, time,
    ///     a request's correlation. The engines' schema had two, so a database both write answered "what happened to this subject" with a scan.
    /// </summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_migrated_audit_trail_has_the_indexes_it_is_read_by(SqlDialect dialect)
    {
        var connectionString = await databases.CreateAsync(dialect, "audited");
        var created = await PragmaticMigrations.MigrateAsync(connectionString, DialectSchemas.For(dialect));
        Assert.True(created.Success, created.Error);

        var entries = (await PragmaticMigrations.IntrospectAsync(dialect, connectionString)).Tables.Single(t => t.Name == "__AuditEntries");
        var indexes = entries.Indexes.Select(i => (i.Name, Columns: string.Join(",", i.Columns))).ToList();
        Assert.Contains(("IX___AuditEntries_SegmentId_Seq", "SegmentId,Seq"), indexes);
        Assert.Contains(("IX___AuditEntries_SubjectRef_OccurredAt", "SubjectRef,OccurredAt"), indexes);
        Assert.Contains(("IX___AuditEntries_OccurredAt", "OccurredAt"), indexes);
        Assert.Contains(("IX___AuditEntries_CorrelationId", "CorrelationId"), indexes);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_schema_evolves_by_a_column_and_an_index_without_losing_rows(SqlDialect dialect)
    {
        var v1 = DialectSchemas.For(dialect);
        var connectionString = await databases.CreateAsync(dialect, "evolve");
        Assert.True((await PragmaticMigrations.MigrateAsync(connectionString, v1)).Success);
        await using (var connection = await PragmaticMigrations.OpenAsync(dialect, connectionString))
        {
            using var session = new SqlSession(connection, dialect);
            await session.InsertAsync(new Category { Name = "Lakes" }); // not [ConcurrencyAware]: a session writes it alone
        }

        // V2: the categories get an icon (optional) and an index on it. Built, not `v1 with { … }`: SchemaVersion caches its hash in a field a
        // `with` copies (MigrationTests in the Showcase).
        var categories = v1.Tables.Single(t => t.Name == "Categories");
        var icon = new ColumnSchema("Icon", dialect switch
        {
            SqlDialect.SqlServer => "nvarchar(64)", SqlDialect.PostgreSql or SqlDialect.MySql => "varchar(64)", SqlDialect.Oracle => "VARCHAR2(64 CHAR)", _ => "TEXT",
        }, true, false);
        var evolved = categories with { Columns = categories.Columns.Add(icon), Indexes = categories.Indexes.Add(new IndexSchema("IX_Categories_Icon", ["Icon"])) };
        var v2 = new SchemaVersion(v1.Tables.Replace(categories, evolved), v1.DatabaseName, v1.ProviderName, v1.ConfigKey);

        var result = await PragmaticMigrations.MigrateAsync(connectionString, v2);
        Assert.True(result.Success, result.Error);
        var applied = result.AppliedChanges.Select(c => c.GetType().Name).Order(StringComparer.Ordinal).ToList();
        Assert.True(applied.SequenceEqual(["AddColumn", "AddIndex"]), $"applied [{string.Join(", ", applied)}]; the script: {result.GeneratedSql}");
        Assert.Empty(await PragmaticMigrations.DiffAsync(dialect, connectionString, v2));

        await using (var connection = await PragmaticMigrations.OpenAsync(dialect, connectionString))
        {
            await using var count = connection.CreateCommand();
            count.CommandText = dialect == SqlDialect.SqlServer ? "SELECT COUNT(*) FROM [Categories]" : "SELECT COUNT(*) FROM \"Categories\"";
            Assert.Equal(1L, Convert.ToInt64(await count.ExecuteScalarAsync()));
        }
    }

    /// <summary>The schema on SQL Server with one of the old forms put back: an unbounded indexed string, or a false default.</summary>
    [Theory]
    [InlineData("nvarchar(max) indexed")]
    [InlineData("DEFAULT false")]
    public async Task On_SQL_Server_Pragmatics_schema_as_written_does_not_migrate(string departure)
    {
        var spike = DialectSchemas.For(SqlDialect.SqlServer);
        ColumnSchema Back(ColumnSchema c) => departure == "DEFAULT false"
            ? c.DefaultValue == "0" ? c with { DefaultValue = "false" } : c
            : c.SqlType == "nvarchar(450)" ? c with { SqlType = "nvarchar(max)" } : c;
        var pragmatic = new SchemaVersion([.. spike.Tables.Select(t => t with { Columns = [.. t.Columns.Select(Back)] })], spike.DatabaseName, spike.ProviderName, spike.ConfigKey);
        Assert.NotEqual(Pragmatic.Migrations.Schema.SchemaHasher.Compute(spike), Pragmatic.Migrations.Schema.SchemaHasher.Compute(pragmatic)); // the departure is in it

        var result = await PragmaticMigrations.MigrateAsync(await databases.CreateAsync(SqlDialect.SqlServer, "pragmatic"), pragmatic);
        Assert.False(result.Success, "Pragmatic's schema as written migrated on SQL Server: the departure is not needed");
        Assert.Contains(departure == "DEFAULT false" ? "false" : "invalid for use as a key column", result.Error, StringComparison.OrdinalIgnoreCase); // and for that reason
    }
}
