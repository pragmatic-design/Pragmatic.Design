using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>SG model for the complete schema metadata of one database.</summary>
internal sealed record SchemaMetadataModel(
    string DatabaseName,
    string Namespace,
    EfCoreProvider Provider,
    EquatableArray<TableSchemaModel> Tables,
    string? ConfigKey = null);
