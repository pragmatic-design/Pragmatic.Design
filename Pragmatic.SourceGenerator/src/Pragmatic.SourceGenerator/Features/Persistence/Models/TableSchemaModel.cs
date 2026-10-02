using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>SG model for a table in the schema metadata output.</summary>
internal sealed record TableSchemaModel(
    string TableName,
    string? SchemaName,
    string? EntityTypeName,
    EquatableArray<ColumnSchemaModel> Columns,
    EquatableArray<IndexSchemaModel> Indexes,
    EquatableArray<ForeignKeySchemaModel> ForeignKeys,
    EquatableArray<CheckConstraintSchemaModel> CheckConstraints = default);

/// <summary>SG model for a row-level invariant the database enforces.</summary>
/// <param name="Name">Constraint name.</param>
/// <param name="Expression">The boolean SQL a row must satisfy.</param>
internal sealed record CheckConstraintSchemaModel(string Name, string Expression);
