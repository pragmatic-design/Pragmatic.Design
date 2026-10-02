namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     What <see cref="TraitParentVisibilityFilterTemplate" /> needs to lift a parent's row
///     restriction onto its trait children.
/// </summary>
/// <param name="Namespace">The namespace both entities live in.</param>
/// <param name="ChildTypeName">The generated child entity — <c>ReservationAttachment</c>.</param>
/// <param name="ParentTypeName">The entity carrying <c>[HasOwner]</c>/<c>[HasAccessScopes]</c>.</param>
/// <param name="ParentNavigationName">The child's reference navigation back to the parent.</param>
/// <param name="BypassPermission">The parent's own bypass permission, reused verbatim.</param>
/// <param name="IsOwned">The parent carries <c>[HasOwner]</c>.</param>
/// <param name="IsScoped">The parent carries <c>[HasAccessScopes]</c>.</param>
internal readonly record struct TraitParentVisibilityModel(
    string Namespace,
    string ChildTypeName,
    string ParentTypeName,
    string ParentNavigationName,
    string BypassPermission,
    bool IsOwned,
    bool IsScoped);
