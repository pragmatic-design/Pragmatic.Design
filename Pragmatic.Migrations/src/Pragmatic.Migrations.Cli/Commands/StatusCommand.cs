using Pragmatic.Migrations.Cli.Discovery;
using Pragmatic.Migrations.Cli.Interaction;
using Pragmatic.Migrations.Cli.Pipeline;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pragmatic.Migrations.Cli.Commands;

/// <summary>
///     Shows current vs desired schema diff without applying changes.
/// </summary>
public sealed class StatusCommand : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings)
    {
        var (schemas, handler, resolver) = CommandHelper.Bootstrap(settings);
        var pipeline = new CliMigrationPipeline(handler, resolver, settings.Verbose, settings.AuditTableName, settings.DropUnknownTables);

        handler.Status("\u25c6 Pragmatic Migrations — Status\n");
        CommandHelper.ShowDatabases(schemas, handler);

        using var cts = ConsoleCancellation.CreateLinkedTokenSource();
        return await pipeline.RunStatusAsync(schemas, settings.DatabaseFilter, cts.Token)
            .ConfigureAwait(false);
    }
}
