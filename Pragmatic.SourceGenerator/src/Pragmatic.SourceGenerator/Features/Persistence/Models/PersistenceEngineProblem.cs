using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>A cache-safe description of one engine choice that cannot stand, reported in the source-output stage.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="Subject">The entity (<see cref="PersistenceEngineProblemKind.NoEngineNamed" />) or the boundary that names the engine.</param>
/// <param name="Boundary">The entity's boundary, or <c>(none)</c>.</param>
/// <param name="Engine">The engine the boundary names, when it names one.</param>
/// <param name="Referenced">The engines the compilation references, for the message.</param>
/// <param name="LocationInfo">Where the fix goes: the entity when no engine is named, the boundary otherwise.</param>
internal sealed record PersistenceEngineProblem(
    PersistenceEngineProblemKind Kind,
    string Subject,
    string Boundary,
    string? Engine,
    string Referenced,
    LocationInfo? LocationInfo);
