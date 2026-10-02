using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Maintenance;
using Pragmatic.Migrations.Configuration;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     <see cref="MigrationRunner"/> leader-election lifecycle: resolves the strategy used to
///     ensure a single node executes migrations in a distributed deployment.
/// </summary>
public sealed partial class MigrationRunner
{
    /// <summary>
    ///     Confirms, from a follower, that the leader actually brought the database to the desired
    ///     schema. Called after the wait returns: the leader releases its lock whether it succeeded
    ///     or failed, so "the lock is free" alone is not evidence the schema is ready.
    /// </summary>
    /// <returns>
    ///     <see cref="MigrationResult.NoChanges" /> when the database matches the desired schema;
    ///     a failed result listing what is still missing otherwise, so the host aborts instead of
    ///     serving traffic against a half-migrated database.
    /// </returns>
    private async Task<MigrationResult> VerifyFollowerSchemaAsync(
        MigrationContext context, string dbName, string? providerName, CancellationToken ct)
    {
        try
        {
            var introspector = providerFactory.GetIntrospector(providerName);
            var connectionFactory = providerFactory.GetConnectionFactory(providerName);

            var connection = await MigrationResilience.OpenWithRetryAsync(
                () => connectionFactory.CreateOpenConnectionAsync(context.ConnectionString, ct), ct).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                var current = await introspector.IntrospectAsync(connection, ct).ConfigureAwait(false);
                var diff = diffEngine.ComputeDiff(context.DesiredSchema, current, !context.Options.ManageDeclaredTablesOnly);

                if (!diff.HasChanges)
                {
                    MigrationDiagnostics.SchemaUpToDate(_logger, dbName, context.DesiredSchema.Hash);
                    return MigrationResult.NoChanges;
                }

                var pending = string.Join(", ", diff.Changes.Take(5).Select(c => c.Description));
                var error =
                    $"The migration leader finished but the schema is still {diff.Changes.Length} change(s) behind " +
                    $"(e.g. {pending}). The leader's migration most likely failed.";

                MigrationDiagnostics.FollowerSchemaStale(_logger, dbName, diff.Changes.Length);
                progressStream?.Report(new MigrationProgressEvent("error",
                    $"[{dbName}] {error}", IsError: true, DatabaseName: dbName));

                return new MigrationResult(false, 0, TimeSpan.Zero, null, diff.Changes, error)
                {
                    Suggestions =
                    [
                        "Check the migration leader's logs — it released the lock without completing the schema.",
                        "Fix the underlying failure and restart; the next instance to start will retry as leader."
                    ]
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Unable to verify is not the same as verified-ok: report it as a failure rather than
            // letting the host start on an unknown schema.
            MigrationDiagnostics.MigrationFailed(_logger, ex, dbName, 0, 0, "follower schema verification");
            return new MigrationResult(false, 0, TimeSpan.Zero, null, [],
                $"Could not verify the schema after waiting for the migration leader: {ex.Message}");
        }
    }

    /// <summary>
    ///     Resolves the leader election strategy. If an explicit one was injected, use it.
    ///     Otherwise create a <see cref="DatabaseLeaderElection"/> using the migration context.
    /// </summary>
    private IMigrationLeaderElection ResolveLeaderElection(MigrationContext context, string? providerName)
    {
        if (_explicitLeaderElection is not null)
            return _explicitLeaderElection;

        // DatabaseLeaderElection needs a real DB connection. In test/mock scenarios
        // where the connection factory may not work, we wrap the election in a fallback.
        var connectionFactory = providerFactory.GetConnectionFactory(providerName);
        if (connectionFactory is null)
            return new AlwaysLeaderElection();

        // SQLite is a single-file / single-process database — distributed leader election is
        // meaningless, and its common in-memory variant opens a fresh database per connection, so a lock
        // table written by one connection isn't visible to another and acquisition can never succeed
        // (the runner would become a follower and wait forever for a leader that doesn't exist). Run as the
        // sole leader instead.
        if (string.Equals(connectionFactory.ProviderName, "Sqlite", StringComparison.OrdinalIgnoreCase))
            return new AlwaysLeaderElection();

        return new FallbackLeaderElection(
            new DatabaseLeaderElection(
                () => connectionFactory.CreateOpenConnectionAsync(context.ConnectionString, default),
                // Derive the leader-election SQL dialect from the RESOLVED connection factory, not
                // from the (possibly-null) requested providerName. Defaulting to "PostgreSql" while the
                // resolved factory is e.g. SQLite produced wrong-dialect polling SQL that always failed —
                // FallbackLeaderElection swallowed it to follower mode and WaitForLeaderCompletionAsync then
                // polled forever (deadlock). The factory's provider always matches the opened connection.
                connectionFactory.ProviderName,
                NullLogger<DatabaseLeaderElection>.Instance,
                // The lease must outlive the migration it protects. It was a fixed 5 minutes while
                // migrations are allowed 30 by default, so a long migration let its own lease
                // expire and a second node could take over mid-flight.
                lockTimeout: context.Options.Timeout,
                pollInterval: context.Options.LeaderPollInterval),
            _logger);
    }
}
