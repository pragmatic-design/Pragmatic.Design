using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>
///     A grid adapter of Pragmatic's on an engine: its fields, the names it withholds, whether it takes nested filters, and the engine's page read
///     and sort.
/// </summary>
internal sealed record GridAdapterModel(
    string Hint,
    string Package,
    string Engine,
    string? Namespace,
    string TypeName,
    string EntityName,
    string EntityType,
    bool Nested,
    EquatableArray<GridFieldModel> Fields,
    EquatableArray<string> Withheld,
    GridLoadModel Load,
    GridOrderingModel Ordering);
