#pragma warning disable CA2007 // ConfigureAwait in test code

using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Pragmatic.Migrations.Cli.Discovery;
using Pragmatic.Migrations.Cli.Interaction;
using Pragmatic.Migrations.Cli.Pipeline;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Core.Tests.Unit.Cli;

/// <summary>
///     End-to-end behaviour of <c>pragmatic-migrate apply</c> / <c>status</c> against a real SQLite
///     database. A CLI that re-implements the execution loop drifts from the runner — it can miss
///     things such as SQLite table rebuilds — so these tests exercise the pipeline itself, not
///     the pieces around it.
/// </summary>
public class CliMigrationPipelineTests : IDisposable
{
    // A single shared file-backed database: the pipeline opens its own connections, so :memory:
    // (a private database per connection) would give each one an empty schema.
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"pragmatic-cli-{Guid.CreateVersion7():N}.db");
    private readonly SqliteConnection _keepAlive;

    public CliMigrationPipelineTests()
    {
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    private string ConnectionString => $"Data Source={_databasePath}";

    public void Dispose()
    {
        _keepAlive.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_databasePath); } catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static SchemaVersion Schema(params TableSchema[] tables) =>
        new([.. tables], DatabaseName: "cli", ProviderName: MigrationConstants.ProviderSqlite);

    private static TableSchema Widgets(bool withNote = false, bool noteNullable = true) =>
        new("Widgets", null,
            withNote
                ? [new ColumnSchema("Id", "TEXT", false, true), new ColumnSchema("Note", "TEXT", noteNullable, false)]
                : [new ColumnSchema("Id", "TEXT", false, true)],
            [], []);

    private (CliMigrationPipeline Pipeline, RecordingHandler Handler) Create(bool confirm = true, bool verbose = false)
    {
        var handler = new RecordingHandler(confirm);
        var resolver = new ConnectionStringResolver(configPath: null, overrideConnectionString: ConnectionString);
        return (new CliMigrationPipeline(handler, resolver, verbose), handler);
    }

    private async Task<SchemaVersion> IntrospectAsync()
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync();
        return await new SqliteSchemaIntrospector().IntrospectAsync(conn);
    }

    [Fact]
    public async Task Apply_EmptyDatabase_CreatesTheSchema()
    {
        var (pipeline, handler) = Create();

        var exitCode = await pipeline.RunApplyAsync([Schema(Widgets())], null,
            dryRun: false, force: false, TimeSpan.FromMinutes(1), CancellationToken.None);

        exitCode.Should().Be(0);
        handler.Errors.Should().BeEmpty();
        (await IntrospectAsync()).Tables.Should().Contain(t => t.Name == "Widgets");
    }

    [Fact]
    public async Task Apply_DryRun_ShowsTheSqlAndChangesNothing()
    {
        var (pipeline, handler) = Create();

        var exitCode = await pipeline.RunApplyAsync([Schema(Widgets())], null,
            dryRun: true, force: false, TimeSpan.FromMinutes(1), CancellationToken.None);

        exitCode.Should().Be(0);
        handler.Sql.Should().Contain(s => s.Contains("CREATE TABLE", StringComparison.Ordinal));
        (await IntrospectAsync()).Tables.Should().BeEmpty("a dry run must not touch the database");
    }

    [Fact]
    public async Task Apply_SecondRun_IsANoOp()
    {
        var (pipeline, _) = Create();
        var schema = Schema(Widgets());

        await pipeline.RunApplyAsync([schema], null, false, false, TimeSpan.FromMinutes(1), CancellationToken.None);
        var (second, handler) = Create();
        var exitCode = await second.RunApplyAsync([schema], null, false, false, TimeSpan.FromMinutes(1), CancellationToken.None);

        exitCode.Should().Be(0);
        handler.Completed.Should().ContainSingle().Which.ChangesApplied.Should().Be(0);
    }

    [Fact]
    public async Task Apply_AppliesASqliteTableRebuild()
    {
        // On SQLite a nullability change is only expressible as a table rebuild, so the CLI has to
        // drive rebuilds too.
        var (first, _) = Create();
        await first.RunApplyAsync([Schema(Widgets(withNote: true, noteNullable: true))], null,
            false, false, TimeSpan.FromMinutes(1), CancellationToken.None);

        var (pipeline, handler) = Create();
        var exitCode = await pipeline.RunApplyAsync([Schema(Widgets(withNote: true, noteNullable: false))], null,
            dryRun: false, force: true, TimeSpan.FromMinutes(1), CancellationToken.None);

        exitCode.Should().Be(0, string.Join(" | ", handler.Errors));

        var note = (await IntrospectAsync()).Tables.First(t => t.Name == "Widgets").Columns.First(c => c.Name == "Note");
        note.IsNullable.Should().BeFalse("the rebuild must actually have been applied");
    }

    [Fact]
    public async Task Apply_BreakingChangeDeclined_CancelsAndChangesNothing()
    {
        var (first, _) = Create();
        await first.RunApplyAsync([Schema(Widgets(withNote: true))], null,
            false, false, TimeSpan.FromMinutes(1), CancellationToken.None);

        // Dropping Note is breaking; the operator says no.
        var (pipeline, handler) = Create(confirm: false);
        var exitCode = await pipeline.RunApplyAsync([Schema(Widgets())], null,
            dryRun: false, force: false, TimeSpan.FromMinutes(1), CancellationToken.None);

        exitCode.Should().Be(1);
        handler.Completed.Should().ContainSingle().Which.Error.Should().Contain("Cancelled by user");
        (await IntrospectAsync()).Tables.First(t => t.Name == "Widgets").Columns
            .Should().Contain(c => c.Name == "Note");
    }

    [Fact]
    public async Task Apply_BreakingChangeConfirmed_IsApplied()
    {
        var (first, _) = Create();
        await first.RunApplyAsync([Schema(Widgets(withNote: true))], null,
            false, false, TimeSpan.FromMinutes(1), CancellationToken.None);

        var (pipeline, handler) = Create(confirm: true);
        var exitCode = await pipeline.RunApplyAsync([Schema(Widgets())], null,
            dryRun: false, force: false, TimeSpan.FromMinutes(1), CancellationToken.None);

        exitCode.Should().Be(0, string.Join(" | ", handler.Errors));
        handler.DataImpactReported.Should().BeTrue("the operator is told how many rows a DROP affects");
        (await IntrospectAsync()).Tables.First(t => t.Name == "Widgets").Columns
            .Should().NotContain(c => c.Name == "Note");
    }

    [Fact]
    public async Task Status_ReportsPendingChangesWithoutApplyingThem()
    {
        var (pipeline, handler) = Create();

        var exitCode = await pipeline.RunStatusAsync([Schema(Widgets())], null, CancellationToken.None);

        exitCode.Should().Be(0);
        handler.Diffs.Should().ContainSingle().Which.Changes.Should().NotBeEmpty();
        (await IntrospectAsync()).Tables.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_UpToDate_ReportsNoChanges()
    {
        var (apply, _) = Create();
        await apply.RunApplyAsync([Schema(Widgets())], null, false, false, TimeSpan.FromMinutes(1), CancellationToken.None);

        var (pipeline, handler) = Create();
        await pipeline.RunStatusAsync([Schema(Widgets())], null, CancellationToken.None);

        handler.Diffs.Should().BeEmpty();
        handler.Status_.Should().Contain(s => s.Contains("Up to date", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_VerboseMode_ShowsTheStatementsBeforeApplyingThem()
    {
        var (pipeline, handler) = Create(verbose: true);

        await pipeline.RunApplyAsync([Schema(Widgets())], null,
            dryRun: false, force: false, TimeSpan.FromMinutes(1), CancellationToken.None);

        handler.Sql.Should().NotBeEmpty("--verbose prints each change before it runs");
    }

    /// <summary>Creates a table the declared schema knows nothing about — another system's.</summary>
    private async Task CreateForeignTableAsync(string name)
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE TABLE \"{name}\" (Id TEXT PRIMARY KEY, Payload TEXT)";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<bool> TableExistsAsync(string name) =>
        (await IntrospectAsync()).Tables.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     A host configured with <c>ManageDeclaredTablesOnly()</c> leaves a table it does not declare
    ///     alone — the case of a database shared with a legacy application. The CLI has no way to be
    ///     told the same thing, computes its diff with the <c>dropUnknownTables: true</c> default and
    ///     calls <c>Force()</c> unconditionally, so it drops what the host preserves.
    /// </summary>
    [Fact]
    public async Task Apply_DoesNotDropATableTheDeclaredSchemaKnowsNothingAbout()
    {
        await CreateForeignTableAsync("LegacyOrders");
        var (pipeline, _) = Create();

        await pipeline.RunApplyAsync([Schema(Widgets())], null,
            dryRun: false, force: true, TimeSpan.FromMinutes(1), CancellationToken.None);

        (await TableExistsAsync("LegacyOrders")).Should()
            .BeTrue("a table the CLI did not create is another system's data, not a leftover");
    }

    /// <summary>
    ///     <c>--audit-table</c> renames the audit table for the runner only: it never reached the
    ///     introspection or the diff, and a custom name matches neither the <c>__</c> prefix nor the
    ///     legacy <c>pragmatic_*</c> list that <c>IsFrameworkTable</c> spares.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The assertion is on the diff the operator is SHOWN, not on the table surviving. The
    ///         runner builds its own introspector through <c>MigrationsBuilder</c>, which already
    ///         excludes the audit table, so the drop never executes and "the table still exists" is
    ///         true whatever the CLI does — a test asserting that passes whether or not the CLI spares the table.
    ///     </para>
    ///     <para>
    ///         Needs <c>--drop-unknown-tables</c>: with the safe default the drop pass does not run at
    ///         all. The audit table is created by the first run — building it by hand would give it the
    ///         wrong shape and the runner would fail inserting its own row before reaching the diff.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task Apply_WithACustomAuditTable_DoesNotOfferToDropItsOwnHistory()
    {
        const string auditTable = "schema_history";
        var resolver = new ConnectionStringResolver(configPath: null, overrideConnectionString: ConnectionString);
        RecordingHandler Run(out CliMigrationPipeline pipeline)
        {
            var h = new RecordingHandler(confirm: true);
            pipeline = new CliMigrationPipeline(h, resolver, verbose: false,
                auditTableName: auditTable, dropUnknownTables: true);
            return h;
        }

        Run(out var first);
        await first.RunApplyAsync([Schema(Widgets())], null,
            dryRun: false, force: true, TimeSpan.FromMinutes(1), CancellationToken.None);
        (await TableExistsAsync(auditTable)).Should().BeTrue("the first run creates the audit table");

        // Second run with a schema change, so the diff is non-empty and the drop pass actually runs.
        var handler = Run(out var second);
        await second.RunApplyAsync([Schema(Widgets(withNote: true))], null,
            dryRun: false, force: true, TimeSpan.FromMinutes(1), CancellationToken.None);

        handler.Diffs.SelectMany(d => d.Changes).OfType<DropTable>()
            .Select(d => d.TableName)
            .Should().NotContain(auditTable,
                "the operator must never be shown a DROP of the table that records the migrations");
    }

    private sealed class RecordingHandler(bool confirm) : IInteractionHandler
    {
        public List<string> Status_ { get; } = [];
        public List<string> Sql { get; } = [];
        public List<SchemaDiff> Diffs { get; } = [];
        public List<MigrationResult> Completed { get; } = [];
        public List<string> Errors { get; } = [];
        public bool DataImpactReported { get; private set; }

        public void Status(string message, StatusLevel level = StatusLevel.Info)
        {
            Status_.Add(message);
            if (message.Contains("Data impact", StringComparison.Ordinal)) DataImpactReported = true;
        }

        public Task<bool> ConfirmAsync(string question, bool defaultValue = false) => Task.FromResult(confirm);
        public Task<int> ChooseAsync(string question, IReadOnlyList<string> options) => Task.FromResult(0);
        public Task<string> PromptAsync(string question, string? defaultValue = null) => Task.FromResult(defaultValue ?? "");
        public void ShowSql(string sql, string? label = null) => Sql.Add(sql);
        public void ShowDiff(SchemaDiff diff) => Diffs.Add(diff);
        public void StartProgress(string label) { }
        public void UpdateProgress(int current, int total, string description, bool isBreaking = false) { }
        public void EndProgress() { }
        public void Complete(MigrationResult result) => Completed.Add(result);

        public void Error(string message, string? detail = null, IReadOnlyList<string>? suggestions = null) =>
            Errors.Add(message);
    }
}
