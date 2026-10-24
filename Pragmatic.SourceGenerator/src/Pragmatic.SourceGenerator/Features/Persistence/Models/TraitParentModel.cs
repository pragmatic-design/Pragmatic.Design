namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     The parent an entity marked <c>[TraitOf&lt;TParent&gt;]</c> is the child of: its name, and the row restrictions the child
///     inherits (<c>ParentVisibilityFilter</c>).
/// </summary>
/// <param name="TypeName">The parent's simple name: the child's reference navigation to it has the same.</param>
/// <param name="IsOwned">The parent carries <c>[HasOwner]</c>.</param>
/// <param name="IsScoped">The parent carries <c>[HasAccessScopes]</c>.</param>
internal sealed record TraitParentModel(string TypeName, bool IsOwned, bool IsScoped);
