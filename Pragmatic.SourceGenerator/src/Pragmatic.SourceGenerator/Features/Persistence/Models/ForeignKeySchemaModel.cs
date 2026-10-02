namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>SG model for a foreign key in the schema metadata output.</summary>
internal sealed record ForeignKeySchemaModel(
    string Name,
    string Column,
    string ReferencedTable,
    string ReferencedColumn,
    string OnDelete);
