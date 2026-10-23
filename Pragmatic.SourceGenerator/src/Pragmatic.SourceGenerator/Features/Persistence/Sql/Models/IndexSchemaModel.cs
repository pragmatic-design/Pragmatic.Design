using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>An index of a migrated table: its columns, whether it is unique, its filter in the dialect.</summary>
internal sealed record IndexSchemaModel(string Name, EquatableArray<string> Columns, bool Unique, string? Filter);
