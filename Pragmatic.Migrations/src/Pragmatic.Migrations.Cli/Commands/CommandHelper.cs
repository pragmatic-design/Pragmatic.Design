using Pragmatic.Migrations.Cli.Discovery;
using Pragmatic.Migrations.Cli.Interaction;
using Pragmatic.Migrations.Schema;
using Spectre.Console;

namespace Pragmatic.Migrations.Cli.Commands;

/// <summary>
///     Shared bootstrap logic for CLI commands: resolve assembly, discover schemas,
///     create interaction handler, resolve connection strings.
/// </summary>
internal static class CommandHelper
{
    internal static (IReadOnlyList<SchemaVersion> Schemas, IInteractionHandler Handler, ConnectionStringResolver Resolver)
        Bootstrap(CommonSettings settings)
    {
        // --no-color: strip colour and ANSI before anything is written, so redirected output and
        // CI logs stay readable.
        if (settings.NoColor)
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors
            });
        }

        // Create interaction handler based on flags
        IInteractionHandler handler = settings switch
        {
            { Agent: true } => new AgentHandler(),
            { Json: true } => new JsonHandler(),
            MigrateSettings { Yes: true } ms => new NonInteractiveHandler(ms.Force),
            _ => new InteractiveHandler()
        };

        // Resolve assembly path: explicit --assembly wins, then --project (built on the spot),
        // then auto-discovery from the current directory.
        var assemblyPath = settings.AssemblyPath;
        if (assemblyPath is null && settings.ProjectPath is not null)
        {
            handler.Status($"Building {Path.GetFileName(settings.ProjectPath)}...", StatusLevel.Debug);
            assemblyPath = ProjectAssemblyResolver.BuildAndResolve(settings.ProjectPath);
        }

        assemblyPath ??= AssemblySchemaDiscovery.AutoDiscoverAssemblyPath();
        if (assemblyPath is null)
        {
            handler.Error(
                "Could not find host assembly.",
                suggestions: [
                    "Use --assembly <path> to specify the built host .dll",
                    "Or --project <path> to build a .csproj and use its output",
                    "Or run from the project directory (auto-discovers .csproj output)"
                ]);
            throw new InvalidOperationException("Assembly not found");
        }

        handler.Status($"Assembly: {Path.GetFileName(assemblyPath)}", StatusLevel.Debug);

        // Discover schemas
        var schemas = AssemblySchemaDiscovery.Discover(assemblyPath);
        if (schemas.Count == 0)
        {
            handler.Error(
                "No SchemaVersion types found in assembly.",
                suggestions: [
                    "Ensure the host project references Pragmatic.Migrations",
                    "Ensure the project is built (the SG generates schema at compile time)"
                ]);
            throw new InvalidOperationException("No schemas found");
        }

        // Connection string resolver
        var resolver = new ConnectionStringResolver(settings.ConfigPath, settings.ConnectionString);
        var configFile = ConnectionStringResolver.ConfigFilePath;
        if (configFile is not null)
            handler.Status($"Config:   {Path.GetFileName(configFile)}", StatusLevel.Debug);

        return (schemas, handler, resolver);
    }

    internal static void ShowDatabases(IReadOnlyList<SchemaVersion> schemas, IInteractionHandler handler)
    {
        if (handler is InteractiveHandler)
        {
            var table = new Table();
            table.AddColumn("#");
            table.AddColumn("Database");
            table.AddColumn("Provider");
            table.AddColumn("Hash");

            for (var i = 0; i < schemas.Count; i++)
            {
                var s = schemas[i];
                table.AddRow(
                    (i + 1).ToString(),
                    s.DatabaseName ?? "default",
                    s.ProviderName ?? "unknown",
                    s.Hash is { Length: >= 8 } h ? h[..8] + "..." : s.Hash ?? "");
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }
        else
        {
            handler.Status($"Found {schemas.Count} database(s):");
            foreach (var s in schemas)
            {
                var shortHash = s.Hash is { Length: >= 8 } h ? h[..8] + "..." : s.Hash ?? "";
                handler.Status($"  {s.DatabaseName ?? "default"} ({s.ProviderName}) hash={shortHash}");
            }
        }
    }
}
