using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>SG model for an index in the schema metadata output.</summary>
internal sealed record IndexSchemaModel(
    string Name,
    EquatableArray<string> Columns,
    bool IsUnique = false,
    string? Filter = null);
