namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A check constraint of a migrated table: its name and its condition.</summary>
internal sealed record CheckSchemaModel(string Name, string Expression);
