using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Input context for a migration run.
/// </summary>
/// <param name="ConnectionString">Database connection string.</param>
/// <param name="DesiredSchema">SG-generated target schema (includes ProviderName for auto-resolution).</param>
/// <param name="Options">Migration options (dry-run, force, timeout).</param>
public sealed record MigrationContext(
    string ConnectionString,
    SchemaVersion DesiredSchema,
    MigrationOptions Options);
