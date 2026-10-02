using Pragmatic.Migrations.Cli.Discovery;
using Pragmatic.Migrations.Cli.Interaction;
using Pragmatic.Migrations.Cli.Pipeline;
using Spectre.Console.Cli;

namespace Pragmatic.Migrations.Cli.Commands;

/// <summary>
///     Default command: apply pending migrations with interactive confirmation.
/// </summary>
public sealed class MigrateCommand : AsyncCommand<MigrateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MigrateSettings settings)
    {
        var (schemas, handler, resolver) = CommandHelper.Bootstrap(settings);
        var pipeline = new CliMigrationPipeline(handler, resolver, settings.Verbose, settings.AuditTableName, settings.DropUnknownTables);

        handler.Status("\u25c6 Pragmatic Migrations\n");
        CommandHelper.ShowDatabases(schemas, handler);

        var timeout = TimeSpan.FromMinutes(settings.TimeoutMinutes);
        using var cts = ConsoleCancellation.CreateLinkedTokenSource();
        var exitCode = await pipeline.RunApplyAsync(
            schemas, settings.DatabaseFilter, settings.DryRun, settings.Force, timeout, cts.Token)
            .ConfigureAwait(false);

        // Exit code 2 is reserved for usage/internal errors. A migration blocked because
        // breaking changes were not confirmed in non-interactive mode is an expected failure → 1.
        return exitCode;
    }
}
