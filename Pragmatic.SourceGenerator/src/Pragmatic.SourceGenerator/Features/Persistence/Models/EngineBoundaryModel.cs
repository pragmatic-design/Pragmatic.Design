namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A boundary the Sql or documents engine persists that holds a module's store: <c>[Audited]</c> entities (the trail's entries are written
///     through its session) or <c>[DataSubject]</c> ones (the subject registry's tables are in its database).
/// </summary>
/// <param name="BoundaryType">The boundary, fully qualified with <c>global::</c>: the key of its session.</param>
/// <param name="Engine">The engine that persists it, <c>Sql</c> or <c>Documents</c> (<c>PersistenceEngineMarker</c>'s names).</param>
internal sealed record EngineBoundaryModel(string BoundaryType, string Engine);
