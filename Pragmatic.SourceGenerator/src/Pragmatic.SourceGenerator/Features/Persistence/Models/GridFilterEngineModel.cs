namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>The engine a <c>[GridFilter]</c>'s entity is on, for the half of the grid filter that engine writes (<c>GridFilterApplyTemplate</c>).</summary>
/// <param name="Package">The engine's package, which signs the generated file (<c>Pragmatic.Persistence.Sql</c>).</param>
/// <param name="Engine">The engine's name in messages (<c>Sql engine</c>).</param>
/// <param name="Artifact">The file's artifact name (<c>SqlGridFilter</c>).</param>
internal sealed record GridFilterEngineModel(string Package, string Engine, string Artifact);
