namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>SG model for a column in the schema metadata output.</summary>
internal sealed record ColumnSchemaModel(
    string Name,
    string SqlType,
    bool IsNullable,
    bool IsPrimaryKey,
    string? DefaultValue = null,
    string? RenamedFrom = null);
