using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Cli.Discovery;
using Pragmatic.Migrations.Cli.Interaction;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Cli.Pipeline;

/// <summary>
///     Orchestrates the CLI migration flow: discover → connect → diff → interact → apply.
///     Unlike MigrationRunner which executes as a single batch, this pipeline pauses
///     for user interaction on breaking changes and shows per-change previews.
/// </summary>
public sealed class CliMigrationPipeline(
    IInteractionHandler handler,
    ConnectionStringResolver connectionResolver,
    bool verbose = false,
    string? auditTableName = null,
    bool dropUnknownTables = false)
{
    /// <summary>
    ///     Whether a table present in the database but absent from the declared schema should be
    ///     dropped. It defaults to <c>false</c>, and that is deliberate: the CLI cannot read the host's
    ///     <c>MigrationsBuilder</c> configuration, so it cannot know whether the host was told to leave
    ///     foreign tables alone with <c>ManageDeclaredTablesOnly()</c>. Guessing "drop" destroys data
    ///     the application never modelled and cannot restore; guessing "keep" leaves an obsolete table
    ///     behind, which the operator can still remove with <c>--drop-unknown-tables</c>. Only one of
    ///     the two mistakes is reversible.
    /// </summary>
    private bool DropUnknownTables => dropUnknownTables;

    /// <summary>
    ///     Runs the status command: shows diff without applying.
    /// </summary>
    public async Task<int> RunStatusAsync(IReadOnlyList<SchemaVersion> schemas, string? databaseFilter, CancellationToken ct)
    {
        foreach (var schema in FilterSchemas(schemas, databaseFilter))
        {
            var dbName = schema.DatabaseName ?? "default";
            var connectionString = connectionResolver.Resolve(dbName, schema.ConfigKey);
            if (connectionString is null)
            {
                handler.Error($"No connection string found for database '{dbName}'");
                continue;
            }

            var factory = ProviderFactoryHelper.CreateConnectionFactory(schema.ProviderName);
            var introspector = ProviderFactoryHelper.CreateIntrospector(schema.ProviderName, auditTableName);
            var diffEngine = new SchemaDiffEngine();

            var connection = await factory.CreateOpenConnectionAsync(connectionString, ct).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                var current = await introspector.IntrospectAsync(connection, ct).ConfigureAwait(false);
                var diff = diffEngine.ComputeDiff(schema, current, DropUnknownTables);

                handler.Status($"\n{dbName} ({schema.ProviderName}):", StatusLevel.Info);

                if (!diff.HasChanges)
                {
                    handler.Status("  Up to date — no changes needed.", StatusLevel.Success);
                    continue;
                }

                handler.ShowDiff(diff);

                if (diff.HasBreakingChanges)
                {
                    var breakingCount = diff.Changes.Count(c => c.IsBreaking);
                    handler.Status($"  {breakingCount} breaking change(s) detected.", StatusLevel.Warning);
                }
            }
        }

        return 0;
    }

    /// <summary>
    ///     Runs the script command: outputs SQL to stdout.
    /// </summary>
    public async Task<int> RunScriptAsync(IReadOnlyList<SchemaVersion> schemas, string? databaseFilter, CancellationToken ct)
    {
        foreach (var schema in FilterSchemas(schemas, databaseFilter))
        {
            var dbName = schema.DatabaseName ?? "default";
            var connectionString = connectionResolver.Resolve(dbName, schema.ConfigKey);
            if (connectionString is null)
            {
                Console.Error.WriteLine($"No connection string for '{dbName}'");
                continue;
            }

            var factory = ProviderFactoryHelper.CreateConnectionFactory(schema.ProviderName);
            var introspector = ProviderFactoryHelper.CreateIntrospector(schema.ProviderName, auditTableName);
            var generator = ProviderFactoryHelper.CreateGenerator(schema.ProviderName);
            var diffEngine = new SchemaDiffEngine();

            var connection = await factory.CreateOpenConnectionAsync(connectionString, ct).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                var current = await introspector.IntrospectAsync(connection, ct).ConfigureAwait(false);
                var diff = diffEngine.ComputeDiff(schema, current, DropUnknownTables);

                if (!diff.HasChanges) continue;

                // Output clean SQL to stdout (status to stderr)
                Console.Out.Write(generator.GenerateScript(diff));
            }
        }

        return 0;
    }

    /// <summary>
    ///     Runs the apply command with interactive confirmation for breaking changes.
    /// </summary>
    public async Task<int> RunApplyAsync(
        IReadOnlyList<SchemaVersion> schemas,
        string? databaseFilter,
        bool dryRun,
        bool force,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var hasErrors = false;

        foreach (var schema in FilterSchemas(schemas, databaseFilter))
        {
            var dbName = schema.DatabaseName ?? "default";
            var connectionString = connectionResolver.Resolve(dbName, schema.ConfigKey);
            if (connectionString is null)
            {
                handler.Error($"No connection string found for database '{dbName}'");
                hasErrors = true;
                continue;
            }

            var result = await ApplyDatabaseAsync(schema, connectionString, dryRun, force, timeout, ct)
                .ConfigureAwait(false);
            handler.Complete(result);

            if (!result.Success)
                hasErrors = true;
        }

        return hasErrors ? 1 : 0;
    }

    private async Task<MigrationResult> ApplyDatabaseAsync(
        SchemaVersion schema, string connectionString, bool dryRun, bool force, TimeSpan timeout, CancellationToken ct)
    {
        var dbName = schema.DatabaseName ?? "default";
        var factory = ProviderFactoryHelper.CreateConnectionFactory(schema.ProviderName);
        var introspector = ProviderFactoryHelper.CreateIntrospector(schema.ProviderName, auditTableName);
        var generator = ProviderFactoryHelper.CreateGenerator(schema.ProviderName);
        var diffEngine = new SchemaDiffEngine();

        handler.Status($"\n{dbName} ({schema.ProviderName})", StatusLevel.Info);

        var connection = await factory.CreateOpenConnectionAsync(connectionString, ct).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            // Acquire advisory lock
            var migrationLock = await MigrationLock.AcquireAsync(connection, schema.ProviderName ?? "", ct)
                .ConfigureAwait(false);
            await using (migrationLock.ConfigureAwait(false))
            {
                // Introspect
                handler.Status("Introspecting current schema...", StatusLevel.Debug);
                var current = await introspector.IntrospectAsync(connection, ct).ConfigureAwait(false);

                // Both hashes come from SchemaHasher, so equal ones mean the diff would find
                // nothing — skip straight to "up to date" without walking the schema.
                if (current.Hash == schema.Hash)
                    return MigrationResult.NoChanges;

                var diff = diffEngine.ComputeDiff(schema, current, DropUnknownTables);
                if (!diff.HasChanges)
                    return MigrationResult.NoChanges;

                handler.ShowDiff(diff);

                // Dry run
                if (dryRun)
                {
                    var sql = generator.GenerateScript(diff);
                    handler.ShowSql(sql, $"{dbName} — Dry Run");
                    return new MigrationResult(true, 0, TimeSpan.Zero, sql, diff.Changes, null);
                }

                if (verbose)
                {
                    foreach (var change in diff.Changes)
                        handler.ShowSql(SafeRenderSql(generator, change), change.Description);
                }

                // Confirm breaking changes
                if (diff.HasBreakingChanges && !force)
                {
                    foreach (var change in diff.Changes.Where(c => c.IsBreaking))
                    {
                        handler.ShowSql(SafeRenderSql(generator, change), change.Description);

                        // Query data impact
                        var impact = await QueryDataImpactAsync(connection, change, ct).ConfigureAwait(false);
                        if (impact is not null)
                            handler.Status($"Data impact: {impact}", StatusLevel.Warning);

                        var proceed = await handler.ConfirmAsync(
                            $"Apply {change.Description}?", defaultValue: false).ConfigureAwait(false);
                        if (!proceed)
                        {
                            handler.Status("Migration cancelled by user.", StatusLevel.Warning);
                            return new MigrationResult(false, 0, TimeSpan.Zero, null,
                                diff.Changes, "Cancelled by user");
                        }
                    }
                }

                // Execute through the SAME MigrationRunner the host uses at startup. The CLI used
                // to re-implement the execution loop here, and the two drifted: the CLI knew
                // nothing about SQLite table rebuilds or post-commit concurrent index builds, so
                // `pragmatic-migrate apply` and a host boot produced different results on the same
                // database. Everything above this point is the CLI's own value — showing the diff,
                // pricing the data impact, asking before a destructive change — and everything
                // below is delegated.
                handler.StartProgress($"Applying {diff.Changes.Length} changes...");

                var result = await RunMigrationAsync(schema, connection, timeout, ct).ConfigureAwait(false);

                handler.EndProgress();

                if (!result.Success)
                {
                    handler.Error(result.Error ?? "Migration failed.", result.FailedChangeSql,
                        result.Suggestions.Length > 0 ? result.Suggestions : null);
                }

                return result;
            }
        }
    }


    /// <summary>
    ///     Runs the migration through the shared <see cref="MigrationRunner" />, wired to the
    ///     already-open connection so it reuses the CLI's session (and its advisory lock).
    /// </summary>
    /// <remarks>
    ///     Only schema changes are applied. <c>IDataMigration</c>s, migration hooks and seed
    ///     providers live in the HOST's DI container, which the CLI does not build — they run when
    ///     the host itself migrates.
    /// </remarks>
    private async Task<MigrationResult> RunMigrationAsync(
        SchemaVersion schema, System.Data.Common.DbConnection connection, TimeSpan timeout, CancellationToken ct)
    {
        var services = new ServiceCollection();
        var builder = new MigrationsBuilder(services);
        builder.UseProvider(schema.ProviderName ?? MigrationConstants.ProviderPostgreSql,
            _ => new CliBorrowedConnection(connection));
        // The operator already confirmed every breaking change (or passed --force) above.
        builder.Force();
        if (auditTableName is not null) builder.UseAuditTable(auditTableName);

        // The runner recomputes the diff from its OWN options — the one the pipeline showed above is
        // only what the operator saw. Without this line the two disagree: the CLI can promise to leave
        // a foreign table alone and the runner drops it anyway, having been asked nothing.
        if (!DropUnknownTables) builder.ManageDeclaredTablesOnly();

        builder.Build();

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<MigrationOptions>() with { Timeout = timeout };

            return await provider.GetRequiredService<IMigrationRunner>()
                .MigrateAsync(new MigrationContext(connection.ConnectionString, schema, options), ct)
                .ConfigureAwait(false);
        }
    }


    /// <summary>
    ///     Renders a change's SQL for display, tolerating a provider that refuses to render it
    ///     standalone (SQLite expresses some changes only as a table rebuild).
    /// </summary>
    private static string SafeRenderSql(ISqlMigrationGenerator generator, SchemaChange change)
    {
        try
        {
            return generator.GenerateChangeScript(change);
        }
        catch (Exception ex)
        {
            return $"-- {change.Description}{Environment.NewLine}-- (applied as part of a table rebuild: {ex.Message})";
        }
    }

    private static async Task<string?> QueryDataImpactAsync(
        System.Data.Common.DbConnection connection, SchemaChange change, CancellationToken ct)
    {
        string? sql = change switch
        {
            DropColumn dc => $"SELECT COUNT(*) FROM \"{dc.TableName}\" WHERE \"{dc.ColumnName}\" IS NOT NULL",
            AlterColumnNullability { NewIsNullable: false } acn =>
                $"SELECT COUNT(*) FROM \"{acn.TableName}\" WHERE \"{acn.ColumnName}\" IS NULL",
            DropTable dt => $"SELECT COUNT(*) FROM \"{dt.TableName}\"",
            _ => null
        };

        if (sql is null) return null;

        try
        {
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = sql;
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                var count = Convert.ToInt64(result);
                return count > 0 ? $"{count:N0} rows affected" : "no data impact";
            }
        }
        catch
        {
            return null; // Query failed — skip impact analysis
        }
    }

    private static IEnumerable<SchemaVersion> FilterSchemas(IReadOnlyList<SchemaVersion> schemas, string? filter) =>
        string.IsNullOrEmpty(filter)
            ? schemas
            : schemas.Where(s =>
                string.Equals(s.DatabaseName, filter, StringComparison.OrdinalIgnoreCase));
}

