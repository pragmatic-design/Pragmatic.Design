namespace Pragmatic.Migrations.Diff.Changes;

/// <summary>Drop an existing table. Always breaking — causes data loss.</summary>
public sealed record DropTable(string TableName, string? SchemaName = null)
    : SchemaChange($"DROP TABLE {TableName}", IsBreaking: true);
