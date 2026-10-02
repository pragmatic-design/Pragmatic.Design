using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Diff.Changes;

/// <summary>Add a foreign key constraint to a table.</summary>
public sealed record AddForeignKey(string TableName, ForeignKeySchema ForeignKey, string? SchemaName = null)
    : SchemaChange($"ADD FK {ForeignKey.Name} ON {TableName}", IsBreaking: false);

/// <summary>Drop an existing foreign key constraint.</summary>
public sealed record DropForeignKey(string TableName, string ForeignKeyName, string? SchemaName = null)
    : SchemaChange($"DROP FK {ForeignKeyName} ON {TableName}", IsBreaking: false);
