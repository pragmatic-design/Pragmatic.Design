using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Diff.Changes;

/// <summary>Create a new table with all its columns, indexes, and foreign keys.</summary>
public sealed record CreateTable(TableSchema Table)
    : SchemaChange($"CREATE TABLE {Table.Name}", IsBreaking: false);
