using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A table of a database's schema, in one dialect.</summary>
internal sealed record TableSchemaModel(string Name, EquatableArray<ColumnSchemaModel> Columns, EquatableArray<IndexSchemaModel> Indexes, EquatableArray<ForeignKeySchemaModel> ForeignKeys);
