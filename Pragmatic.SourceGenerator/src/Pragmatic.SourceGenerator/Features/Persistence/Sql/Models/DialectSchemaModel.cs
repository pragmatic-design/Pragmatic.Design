using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A database's schema in one dialect, and the Pragmatic.Migrations provider that migrates it.</summary>
internal sealed record DialectSchemaModel(string Dialect, string Provider, EquatableArray<TableSchemaModel> Tables);
