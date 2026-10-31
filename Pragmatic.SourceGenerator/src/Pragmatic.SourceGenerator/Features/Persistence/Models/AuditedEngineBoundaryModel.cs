namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A boundary the Sql or documents engine persists with an <c>[Audited]</c> entity: its saves write the trail's entries through its
///     session, so the trail can be read there.
/// </summary>
/// <param name="BoundaryType">The boundary, fully qualified with <c>global::</c>: the key of its session.</param>
/// <param name="Engine">The engine that persists it, <c>Sql</c> or <c>Documents</c> (<c>PersistenceEngineMarker</c>'s names).</param>
internal sealed record AuditedEngineBoundaryModel(string BoundaryType, string Engine);
