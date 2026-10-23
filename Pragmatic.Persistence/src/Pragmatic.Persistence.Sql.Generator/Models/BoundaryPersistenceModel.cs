using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A boundary's <c>Add{Boundary}Persistence</c>: its unit of work keyed by its type, its entities' repositories, on one engine.</summary>
internal sealed record BoundaryPersistenceModel(string Hint, string? Namespace, string BoundaryName, string BoundaryType, EngineRegistrationModel Engine, EquatableArray<string> EntityTypes);
