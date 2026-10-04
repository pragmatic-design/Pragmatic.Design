# pragmatic-migrate

Interactive CLI for Pragmatic.Migrations: schema diff, preview, apply with rich terminal UX.

## Install

```bash
dotnet tool install -g Pragmatic.Migrations.Cli
```

## Commands

```bash
pragmatic-migrate apply     # Apply pending migrations (default, interactive)
pragmatic-migrate status    # Show schema diff without applying
pragmatic-migrate script    # Output SQL to stdout for DBA review
pragmatic-migrate snapshot  # Write the desired schema to committed schema/<db>.schema.json
pragmatic-migrate history   # Show migration audit history
pragmatic-migrate manifest  # Export the manifest JSON embedded in a compiled assembly
pragmatic-migrate generate  # Generate a typed client (C# or TypeScript) from manifest data
```

`apply` shows the diff, prices the data impact of each breaking change and asks before applying it,
then hands execution to the same `MigrationRunner` the host uses at startup, so the CLI and a host
boot can never diverge. It applies **schema changes only**: `IDataMigration`s, migration hooks and
seed providers live in the host's DI container and run when the host itself migrates.

## Quick Start

```bash
# From your project directory (auto-discovers assembly + appsettings.json)
pragmatic-migrate status

# With explicit paths
pragmatic-migrate status --assembly bin/Debug/net10.0/MyApp.dll --config appsettings.json

# Or point at the project and let the CLI build it
pragmatic-migrate status --project src/MyApp.Host

# Apply interactively (asks confirmation for breaking changes)
pragmatic-migrate apply --assembly bin/Debug/net10.0/MyApp.dll

# CI/CD mode (non-interactive)
pragmatic-migrate apply --yes --force --assembly bin/Debug/net10.0/MyApp.dll

# Preview without applying
pragmatic-migrate apply --dry-run

# Generate SQL script for DBA review
pragmatic-migrate script --assembly bin/Debug/net10.0/MyApp.dll > migration.sql

# Write the committed schema snapshot (for PR review + CI drift gate)
pragmatic-migrate snapshot --assembly bin/Debug/net10.0/MyApp.dll --output schema
```

## Options

| Flag | Description |
|------|-------------|
| `--assembly <path>` | Path to built host .dll containing SchemaVersion types |
| `--project <path>` | Path to a .csproj; builds it and uses its output assembly |
| `--config <path>` | Path to appsettings.json for connection strings |
| `--connection <string>` | Override connection string for all databases |
| `--database <name>` | Filter to specific database |
| `--audit-table <name>` | Audit table name, when the host renamed it with `UseAuditTable` |
| `--dry-run` | Show changes without applying |
| `--force` | Skip breaking change confirmation |
| `--yes` | Non-interactive mode (CI/CD) |
| `--json` | NDJSON output for programmatic consumption |
| `--agent` | Bidirectional NDJSON for AI-agent integration (reads stdin, writes stdout) |
| `--verbose` | Show the SQL of each change before it is applied |
| `--no-color` | Disable colour and ANSI output (redirected logs, CI) |
| `--timeout <min>` | Per-statement command timeout in minutes (default: 30) |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success or no changes needed |
| 1 | Error (connection, SQL, timeout) |
| 2 | Usage / internal error |

A migration blocked because breaking changes were not confirmed is an expected failure and exits 1.

## Connection String Resolution

Priority order:
1. `--connection` CLI flag (overrides all)
2. `ConfigKey` from `[PragmaticDatabase]` attribute (auto-resolved from appsettings.json)
3. `ConnectionStrings:{DatabaseName}` convention
4. `ConnectionStrings:Default` fallback

## Modes

### Interactive (default)
Rich terminal UI with colored diffs, SQL preview panels, breaking change confirmation with data impact analysis.

### CI/CD (`--yes`)
Auto-confirms safe changes, blocks on breaking changes unless `--force`.

### JSON (`--json`)
NDJSON output to stdout for programmatic consumption (unidirectional).

### Agent (`--agent`)
Bidirectional NDJSON: reads decisions from stdin and writes events to stdout, so an agent can drive
an interactive apply.

## Security

`--assembly` and `--project` cause the CLI to **load and execute** the target assembly: reading the
SG-generated `SchemaVersion` means reading a static field, which runs the type's static
constructor. Only point it at assemblies you built and trust.
