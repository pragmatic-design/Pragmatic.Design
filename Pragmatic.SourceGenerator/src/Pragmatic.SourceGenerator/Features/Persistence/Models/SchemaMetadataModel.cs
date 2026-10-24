using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>SG model for the complete schema metadata of one database.</summary>
/// <param name="DatabaseName">The database, as the topology names it (<c>Default</c> without one): the schema is <c>{DatabaseName}Schema</c>.</param>
/// <param name="Namespace">The namespace the schema is declared in.</param>
/// <param name="Tables">Its tables, every engine's.</param>
/// <param name="ConfigKey">The connection string's configuration key, from the topology.</param>
/// <param name="ProviderName">
///     The Pragmatic.Migrations provider that migrates it (<c>PostgreSql</c>, <c>SqlServer</c>, <c>Sqlite</c>, <c>MySql</c>, <c>Oracle</c>,
///     or <c>Generic</c>): a name, because EF Core's provider and the Sql engine's dialect are two enums that do not cover the same databases.
/// </param>
internal sealed record SchemaMetadataModel(
    string DatabaseName,
    string Namespace,
    string ProviderName,
    EquatableArray<TableSchemaModel> Tables,
    string? ConfigKey = null);
