using System.ComponentModel;
using Spectre.Console.Cli;

namespace Pragmatic.Migrations.Cli.Commands;

/// <summary>
///     Shared CLI settings for all commands.
/// </summary>
public class CommonSettings : CommandSettings
{
    [CommandOption("--assembly <PATH>")]
    [Description("Path to built host assembly (.dll) containing SG-generated SchemaVersion types.")]
    public string? AssemblyPath { get; set; }

    [CommandOption("--project <PATH>")]
    [Description("Path to .csproj file. Builds the project and loads the output assembly.")]
    public string? ProjectPath { get; set; }

    [CommandOption("--connection <STRING>")]
    [Description("Override connection string for all databases.")]
    public string? ConnectionString { get; set; }

    [CommandOption("--config <PATH>")]
    [Description("Path to appsettings.json for connection string resolution.")]
    public string? ConfigPath { get; set; }

    [CommandOption("--database <NAME>")]
    [Description("Filter to a specific database by name.")]
    public string? DatabaseFilter { get; set; }

    [CommandOption("--audit-table <NAME>")]
    [Description("Audit table name, when the host renamed it with UseAuditTable. Default: __PragmaticSchema.")]
    public string? AuditTableName { get; set; }

    /// <summary>
    ///     Off by default. The CLI cannot read the host's <c>MigrationsBuilder</c> configuration, so it
    ///     cannot tell a table the application dropped from the schema apart from one belonging to
    ///     another system — and only one of those two mistakes can be undone.
    /// </summary>
    [CommandOption("--drop-unknown-tables")]
    [Description("Drop tables present in the database but absent from the declared schema. Off by default: a table the CLI did not create may belong to another system.")]
    [DefaultValue(false)]
    public bool DropUnknownTables { get; set; }

    [CommandOption("--json")]
    [Description("Output structured NDJSON for programmatic consumption (unidirectional).")]
    [DefaultValue(false)]
    public bool Json { get; set; }

    [CommandOption("--agent")]
    [Description("Bidirectional NDJSON mode for AI agent integration (reads stdin, writes stdout).")]
    [DefaultValue(false)]
    public bool Agent { get; set; }

    [CommandOption("--verbose")]
    [Description("Show SQL for each change as it executes.")]
    [DefaultValue(false)]
    public bool Verbose { get; set; }

    [CommandOption("--no-color")]
    [Description("Disable terminal colors.")]
    [DefaultValue(false)]
    public bool NoColor { get; set; }
}

/// <summary>
///     Settings for the apply/migrate command.
/// </summary>
public sealed class MigrateSettings : CommonSettings
{
    [CommandOption("--dry-run")]
    [Description("Show what would change without applying.")]
    [DefaultValue(false)]
    public bool DryRun { get; set; }

    [CommandOption("--force")]
    [Description("Apply breaking changes without per-change confirmation.")]
    [DefaultValue(false)]
    public bool Force { get; set; }

    [CommandOption("--yes")]
    [Description("Non-interactive mode: auto-confirm safe changes, block breaking (unless --force).")]
    [DefaultValue(false)]
    public bool Yes { get; set; }

    [CommandOption("--timeout <MINUTES>")]
    [Description("Migration timeout in minutes. Default: 30.")]
    [DefaultValue(30)]
    public int TimeoutMinutes { get; set; }
}
