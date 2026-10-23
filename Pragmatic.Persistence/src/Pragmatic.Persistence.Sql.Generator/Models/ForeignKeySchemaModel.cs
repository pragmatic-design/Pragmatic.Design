namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A foreign key of a migrated table, with no referential action: the graph deletes say what goes with a row.</summary>
internal sealed record ForeignKeySchemaModel(string Name, string Column, string Table, string ReferencedColumn);
