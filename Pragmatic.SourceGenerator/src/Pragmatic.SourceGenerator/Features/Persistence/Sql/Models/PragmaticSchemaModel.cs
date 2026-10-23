using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     <c>DefaultSchema</c>, the schema of Pragmatic's entities the Sql engine serves: the dialect of the referenced driver (<see cref="Current" />,
///     null when none is), and the tables on each dialect.
/// </summary>
internal sealed record PragmaticSchemaModel(string? Namespace, int EntityCount, string? Current, EquatableArray<DialectSchemaModel> Dialects);
