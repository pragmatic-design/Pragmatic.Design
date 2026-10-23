namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A foreign key of a migrated table, with its referential action (<c>NoAction</c> unless said: the graph deletes say what goes with a row).</summary>
internal sealed record ForeignKeySchemaModel(string Name, string Column, string Table, string ReferencedColumn, string OnDelete = "NoAction");
