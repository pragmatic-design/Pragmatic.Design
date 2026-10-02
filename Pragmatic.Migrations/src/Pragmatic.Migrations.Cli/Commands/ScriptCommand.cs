using Pragmatic.Migrations.Cli.Discovery;
using Pragmatic.Migrations.Cli.Interaction;
using Pragmatic.Migrations.Cli.Pipeline;
using Spectre.Console.Cli;

namespace Pragmatic.Migrations.Cli.Commands;

/// <summary>
///     Outputs SQL migration script to stdout for DBA review or manual execution.
/// </summary>
public sealed class ScriptCommand : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings)
    {
        var (schemas, handler, resolver) = CommandHelper.Bootstrap(settings);
        var pipeline = new CliMigrationPipeline(handler, resolver, settings.Verbose, settings.AuditTableName, settings.DropUnknownTables);

        // Script outputs SQL to stdout — diagnostics go to stderr
        using var cts = ConsoleCancellation.CreateLinkedTokenSource();
        return await pipeline.RunScriptAsync(schemas, settings.DatabaseFilter, cts.Token)
            .ConfigureAwait(false);
    }
}
