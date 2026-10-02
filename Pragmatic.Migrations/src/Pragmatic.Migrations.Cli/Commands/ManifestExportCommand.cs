#pragma warning disable CA2007

using System.ComponentModel;
using System.Text.Json;
using Pragmatic.Migrations.Cli.ClientGen;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pragmatic.Migrations.Cli.Commands;

/// <summary>
///     Exports the manifest JSON from a compiled assembly to a file.
/// </summary>
public sealed class ManifestExportCommand : AsyncCommand<ManifestExportCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--assembly <PATH>")]
        [Description("Path to built assembly containing PragmaticManifest.")]
        public required string AssemblyPath { get; set; }

        [CommandOption("--output <PATH>")]
        [Description("Output path for manifest.json. Default: ./manifest.json")]
        [DefaultValue("manifest.json")]
        public string OutputPath { get; set; } = "manifest.json";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var manifests = ManifestClientReader.ReadFromAssembly(settings.AssemblyPath);
        if (manifests.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No manifests found in assembly.[/]");
            return 1;
        }

        // Single manifest → object, multiple → array
        var toSerialize = manifests.Count == 1 ? (object)manifests[0] : manifests;
        var json = JsonSerializer.Serialize(toSerialize, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        await File.WriteAllTextAsync(settings.OutputPath, json);
        AnsiConsole.MarkupLine($"[green]Exported {manifests.Count} manifest(s) to {settings.OutputPath}[/]");
        return 0;
    }
}
