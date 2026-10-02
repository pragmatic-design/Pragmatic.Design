using Pragmatic.Migrations.Cli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pragmatic.Migrations.Cli;

/// <summary>
///     Entry point for the Pragmatic Migrations CLI.
///     Registers commands and configures Spectre.Console.Cli.
/// </summary>
public static class CliApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("pragmatic-migrate");
            config.SetApplicationVersion("0.1.0");

            // A command that throws reaches the handler below. Left to CommandApp, it ended the process
            // with -1 and a rendering the quiet build that runs `snapshot` never shows — Time off's build
            // failed at random with "exit code -1" and nothing else.
            config.PropagateExceptions();

            config.AddCommand<MigrateCommand>("apply")
                .WithDescription("Apply pending migrations (default command).")
                .WithExample("apply", "--assembly", "bin/Debug/net10.0/MyApp.dll")
                .WithExample("apply", "--dry-run")
                .WithExample("apply", "--yes", "--force");

            config.AddCommand<StatusCommand>("status")
                .WithDescription("Show current vs desired schema diff without applying.")
                .WithExample("status", "--assembly", "bin/Debug/net10.0/MyApp.dll");

            config.AddCommand<ScriptCommand>("script")
                .WithDescription("Output SQL migration script to stdout.")
                .WithExample("script", "--assembly", "bin/Debug/net10.0/MyApp.dll", ">", "migration.sql");

            config.AddCommand<SnapshotCommand>("snapshot")
                .WithDescription("Write the desired schema to committed schema/<db>.schema.json files.")
                .WithExample("snapshot", "--assembly", "bin/Debug/net10.0/MyApp.dll", "--output", "schema");

            config.AddCommand<HistoryCommand>("history")
                .WithDescription("Show migration audit history from __PragmaticSchema.")
                .WithExample("history", "--assembly", "bin/Debug/net10.0/MyApp.dll");

            config.AddCommand<ManifestExportCommand>("manifest")
                .WithDescription("Export manifest JSON from a compiled assembly.")
                .WithExample("manifest", "--assembly", "bin/Debug/net10.0/MyApp.dll", "--output", "manifest.json");

            config.AddCommand<GenerateClientCommand>("generate")
                .WithDescription("Generate C# client from manifest.")
                .WithExample("generate", "--assembly", "bin/Debug/net10.0/MyApp.dll", "--output", "./Client/");
        });

        try
        {
            return await app.RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red bold]Error:[/] {Markup.Escape(ex.GetType().Name)}: {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
