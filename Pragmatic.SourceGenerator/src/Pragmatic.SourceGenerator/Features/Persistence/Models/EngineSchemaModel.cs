using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     The tables an engine other than EF Core contributes to one database's schema: they are written in the same
///     <c>{Database}Schema</c> as the EF Core entities of that database (<see cref="SchemaFeature" />), in its provider's types.
/// </summary>
/// <param name="DatabaseName">The database, as the topology names it (<c>Default</c> without one).</param>
/// <param name="Namespace">The namespace the schema is declared in when no EF Core entity shares the database.</param>
/// <param name="ProviderName">The Pragmatic.Migrations provider the tables are typed for.</param>
/// <param name="ConfigKey">The connection string's configuration key, from the topology.</param>
/// <param name="Tables">The tables, the join tables with them.</param>
internal sealed record EngineSchemaModel(
    string DatabaseName,
    string Namespace,
    string ProviderName,
    string? ConfigKey,
    EquatableArray<TableSchemaModel> Tables);
