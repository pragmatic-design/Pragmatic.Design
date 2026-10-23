using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A <c>[SqlDatabase]</c> class's schema for Pragmatic.Migrations: one per dialect it migrates, with the database's configuration key.</summary>
internal sealed record DatabaseSchemaModel(string? Namespace, string Name, string? ConfigKey, int TableCount, EquatableArray<DialectSchemaModel> Dialects) : TrailerSectionModel(Namespace);
