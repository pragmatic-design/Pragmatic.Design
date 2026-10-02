#pragma warning disable CA2007

using System.ComponentModel;
using Pragmatic.Migrations.Cli.ClientGen;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pragmatic.Migrations.Cli.Commands;

/// <summary>
///     Generates a typed client project from manifest data.
///     Supports C# (default) and TypeScript via --language flag.
/// </summary>
public sealed class GenerateClientCommand : AsyncCommand<GenerateClientCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--assembly <PATH>")]
        [Description("Path to built assembly containing PragmaticManifest.")]
        public string? AssemblyPath { get; set; }

        [CommandOption("--manifest <PATH>")]
        [Description("Path to manifest.json file (alternative to --assembly).")]
        public string? ManifestPath { get; set; }

        [CommandOption("--output <PATH>")]
        [Description("Output directory for generated client project.")]
        public required string OutputPath { get; set; }

        [CommandOption("--namespace <NS>")]
        [Description("Namespace/package name for generated code. Default: derived from manifest assembly name.")]
        public string? Namespace { get; set; }

        [CommandOption("--boundary <NAME>")]
        [Description("Filter to specific boundary.")]
        public string? Boundary { get; set; }

        [CommandOption("--language <LANG>")]
        [Description("Target language: csharp (default), typescript.")]
        [DefaultValue("csharp")]
        public string Language { get; set; } = "csharp";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        IReadOnlyList<ClientManifest> manifests;

        if (!string.IsNullOrEmpty(settings.ManifestPath))
        {
            manifests = [ManifestClientReader.ReadFromFile(settings.ManifestPath!)];
        }
        else if (!string.IsNullOrEmpty(settings.AssemblyPath))
        {
            manifests = ManifestClientReader.ReadFromAssembly(settings.AssemblyPath!);
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Specify --assembly or --manifest[/]");
            return 1;
        }

        if (manifests.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No manifests found.[/]");
            return 1;
        }

        var language = settings.Language.ToLowerInvariant();

        foreach (var source in manifests)
        {
            // Filter by boundary if specified — into a NEW manifest, never mutating the
            // deserialized instance (it may be shared/cached by the reader).
            var manifest = source;
            if (!string.IsNullOrEmpty(settings.Boundary) && source.Endpoints is not null)
            {
                manifest = new ClientManifest
                {
                    Assembly = source.Assembly,
                    Version = source.Version,
                    Endpoints = source.Endpoints
                        .Where(e => e.OperationId?.StartsWith(settings.Boundary!, StringComparison.OrdinalIgnoreCase) == true)
                        .ToList(),
                    Types = source.Types,
                    Actions = source.Actions,
                    Permissions = source.Permissions
                };
            }

            var ns = settings.Namespace ?? (language == "typescript"
                ? $"@pragmatic/{manifest.Assembly?.Split('.').Last()?.ToLowerInvariant() ?? "client"}-client"
                : $"{manifest.Assembly}.Client");

            switch (language)
            {
                case "typescript" or "ts":
                    new TypeScriptClientGenerator(manifest, ns, settings.OutputPath).Generate();
                    break;
                case "csharp" or "cs":
                default:
                    new CSharpClientGenerator(manifest, ns, settings.OutputPath).Generate();
                    break;
            }

            var endpointCount = manifest.Endpoints?.Count ?? 0;
            var typeCount = manifest.Types?.Count ?? 0;
            AnsiConsole.MarkupLine(
                $"[green]Generated {language} client for {manifest.Assembly}:[/] {endpointCount} endpoints, {typeCount} types → {settings.OutputPath}");
        }

        return await Task.FromResult(0);
    }
}
