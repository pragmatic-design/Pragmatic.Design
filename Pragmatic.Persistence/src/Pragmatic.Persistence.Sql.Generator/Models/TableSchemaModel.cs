using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A table of a database's schema, in one dialect: in <see cref="Schema" /> (null: the connection's default), with its checks.</summary>
internal sealed record TableSchemaModel(
    string Name,
    EquatableArray<ColumnSchemaModel> Columns,
    EquatableArray<IndexSchemaModel> Indexes,
    EquatableArray<ForeignKeySchemaModel> ForeignKeys,
    string? Schema = null,
    EquatableArray<CheckSchemaModel> Checks = default);
