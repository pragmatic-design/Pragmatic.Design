#pragma warning disable CA2007

using System.ComponentModel;
using Pragmatic.Migrations.Cli.Discovery;
using Pragmatic.Migrations.Cli.Snapshot;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pragmatic.Migrations.Cli.Commands;

/// <summary>
///     Writes the SG-generated desired schema to deterministic JSON files under a
///     committed <c>schema/</c> folder. The snapshot is the reviewable artifact: it
///     surfaces schema changes in pull requests and turns multi-branch schema
///     conflicts into ordinary merge conflicts. A CI gate
///     (<c>git diff --exit-code schema/</c>) then fails when entities change but the
///     snapshot was not regenerated.
/// </summary>
public sealed class SnapshotCommand : AsyncCommand<SnapshotCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--assembly <PATH>")]
        [Description("Path to the built host assembly (.dll). Auto-discovered from the current directory if omitted.")]
        public string? AssemblyPath { get; set; }

        [CommandOption("--output <DIR>")]
        [Description("Output directory for <database>.schema.json files. Default: ./schema")]
        [DefaultValue("schema")]
        public string OutputDirectory { get; set; } = "schema";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var assemblyPath = settings.AssemblyPath ?? AssemblySchemaDiscovery.AutoDiscoverAssemblyPath();
        if (assemblyPath is null)
        {
            AnsiConsole.MarkupLine(
                "[red]Could not find host assembly.[/] Use --assembly <path> or run from the project directory.");
            return 1;
        }

        var schemas = AssemblySchemaDiscovery.Discover(assemblyPath);
        if (schemas.Count == 0)
        {
            AnsiConsole.MarkupLine(
                "[red]No SchemaVersion types found in assembly.[/] Ensure the project references Pragmatic.Migrations and is built.");
            return 1;
        }

        Directory.CreateDirectory(settings.OutputDirectory);

        foreach (var schema in schemas)
        {
            var dbName = schema.DatabaseName ?? "default";
            var content = SchemaSnapshotSerializer.Serialize(schema);
            var path = Path.Combine(settings.OutputDirectory, $"{dbName}.schema.json");

            if (await SchemaSnapshotWriter.WriteAsync(path, content))
                AnsiConsole.MarkupLine($"[green]wrote[/] {path} [grey](hash {schema.Hash})[/]");
            else
                AnsiConsole.MarkupLine($"[grey]unchanged[/] {path} [grey](hash {schema.Hash})[/]");
        }

        return 0;
    }
}
