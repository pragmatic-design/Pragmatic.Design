using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     The shape of a declared query of Pragmatic's on an engine: its interface, the refused <c>Apply</c>, and, for a query the engine compiles,
///     its filters as a specification and the engine's ordering (<see cref="SqlOrdering" /> on the Sql engine).
/// </summary>
internal sealed record QueryShapeModel(
    string Hint,
    string Package,
    string Engine,
    string? Namespace,
    string TypeName,
    bool Paged,
    string EntityType,
    string ResultType,
    bool Compiled,
    EquatableArray<QueryConditionModel> Conditions,
    SqlOrderingModel? SqlOrdering);
