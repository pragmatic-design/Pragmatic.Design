namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
///     A navigation of an entity a trait generates to another one it generates (a tag link's <c>Tag</c>), its foreign key
///     (<c>{Navigation}Id</c>) among the entity's columns (<see cref="TraitEngineEntityModel" />).
/// </summary>
/// <param name="Navigation">The navigation (<c>Tag</c>).</param>
/// <param name="TargetFullTypeName">The entity it reaches, fully qualified without <c>global::</c>.</param>
internal sealed record TraitEngineReferenceModel(string Navigation, string TargetFullTypeName);
