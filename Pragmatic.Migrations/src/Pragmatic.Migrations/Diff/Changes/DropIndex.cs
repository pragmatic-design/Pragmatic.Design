namespace Pragmatic.Migrations.Diff.Changes;

/// <summary>Drop an existing index from a table.</summary>
public sealed record DropIndex(string TableName, string IndexName, string? SchemaName = null)
    : SchemaChange($"DROP INDEX {IndexName} ON {TableName}", IsBreaking: false);
