namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>The parent of a trait's child an engine persists (<see cref="TraitEngineEntityModel" />).</summary>
/// <param name="TypeName">The parent, which names the reference navigation too.</param>
/// <param name="FullTypeName">The parent, fully qualified without <c>global::</c>.</param>
/// <param name="ForeignKey">The foreign key to the parent (<c>ReviewId</c>).</param>
/// <param name="KeyType">The parent's key type, fully qualified without <c>global::</c> (<c>System.Guid</c>).</param>
internal sealed record TraitEngineParentModel(string TypeName, string FullTypeName, string ForeignKey, string KeyType);
