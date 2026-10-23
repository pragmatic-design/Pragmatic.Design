namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A column of a migrated table: its name, its type in the dialect, whether it takes null, whether it is part of the key, its default.</summary>
internal sealed record ColumnSchemaModel(string Name, string Type, bool Nullable, bool Key, string? DefaultValue);
