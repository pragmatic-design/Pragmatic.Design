using Pragmatic.Migrations.Cli;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 13 — the Pragmatic.Migrations CLI (project <c>Pragmatic.Migrations.Cli</c>,
///     tool name <c>pragmatic-migrate</c>). It exposes one Spectre.Console command per operation:
///     <c>apply</c>, <c>status</c>, <c>script</c>, <c>snapshot</c>, <c>history</c>,
///     <c>manifest</c>, <c>generate</c>.
///     <para>
///     The commands run against a live database / host assembly (connection string + provider),
///     so this sample invokes the CLI entry point in-process with <c>--help</c> to render the real
///     command surface, then documents typical usage. <see cref="CliApp.RunAsync" /> is the exact
///     entry point the published tool uses.
///     </para>
/// </summary>
public static class CliCommandsSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Scenario 13: CLI commands (pragmatic-migrate) ---");

        // Invoke the real CLI entry point in-process — proves the command tree is wired correctly.
        Console.WriteLine("  `pragmatic-migrate --help`:");
        await CliApp.RunAsync(["--help"]);

        Console.WriteLine();
        Console.WriteLine("  Typical usage (against a real database / host assembly):");
        Console.WriteLine("    pragmatic-migrate status   --assembly bin/Debug/net10.0/App.dll");
        Console.WriteLine("    pragmatic-migrate script   --assembly bin/Debug/net10.0/App.dll > migration.sql");
        Console.WriteLine("    pragmatic-migrate apply    --assembly bin/Debug/net10.0/App.dll --yes");
        Console.WriteLine("    pragmatic-migrate history  --assembly bin/Debug/net10.0/App.dll");
        Console.WriteLine("    pragmatic-migrate snapshot --assembly bin/Debug/net10.0/App.dll --output schema");
        Console.WriteLine("    pragmatic-migrate manifest --assembly bin/Debug/net10.0/App.dll --output manifest.json");
        Console.WriteLine("    pragmatic-migrate generate --assembly bin/Debug/net10.0/App.dll --output ./Client/");
        Console.WriteLine();
    }
}
