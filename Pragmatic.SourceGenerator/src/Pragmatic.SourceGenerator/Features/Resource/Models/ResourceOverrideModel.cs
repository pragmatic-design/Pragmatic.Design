using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Resource.Models;

/// <summary>
///     What a developer's partial declaration says about an operation <c>[Resource]</c> scaffolds.
/// </summary>
/// <remarks>
///     The customisation story for the scaffolding: every operation is its own type, so decorating one
///     changes one — permissions for Read need not be the permissions for Delete. What is here
///     <b>replaces</b> the default rather than adding to it, which is why the generated part carries no
///     <c>[RequirePermission]</c> of its own: attributes on partial parts combine, so a default written
///     as an attribute could only ever be tightened.
/// </remarks>
internal sealed record ResourceOverrideModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }

    /// <summary>Namespace + name — the identity the scaffolded type is matched by.</summary>
    public string Key => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    public EquatableArray<string> RequireAllPermissions { get; init; } = EquatableArray<string>.Empty;
    public EquatableArray<string> RequireAnyPermissions { get; init; } = EquatableArray<string>.Empty;
    public EquatableArray<string> UnresolvedRequireAllPaths { get; init; } = EquatableArray<string>.Empty;
    public EquatableArray<string> UnresolvedRequireAnyPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>No permission at all — the developer opening an operation deliberately.</summary>
    public bool AllowAnonymous { get; init; }

    public string? PolicyTypeFullName { get; init; }

    /// <summary>
    ///     The shape the operation answers with, from <c>[ReturnsDto&lt;T&gt;]</c>. Null when the
    ///     developer left the scaffolded one in place.
    /// </summary>
    public ResourceDeclaredDto? DeclaredDto { get; init; }

    /// <summary>
    ///     The filters the developer declared as properties on their own part, replacing the convention.
    /// </summary>
    /// <remarks>
    ///     Declared as ordinary properties carrying <c>[Filter]</c> — the same vocabulary a hand-written
    ///     query uses, checked by the compiler. An attribute listing property names would have been a
    ///     second vocabulary for something that already has one, and a typo in it would produce nothing
    ///     rather than an error.
    /// </remarks>
    public EquatableArray<Persistence.Models.QueryPropertyModel> DeclaredFilters { get; init; } =
        EquatableArray<Persistence.Models.QueryPropertyModel>.Empty;

    /// <summary>Where the declaration is, for PRAG2606 when it matches nothing.</summary>
    public LocationInfo? LocationInfo { get; init; }

    /// <summary>Whether this declaration says anything at all about authorization.</summary>
    public bool HasAuthorization =>
        AllowAnonymous
        || PolicyTypeFullName is not null
        || !RequireAllPermissions.IsDefaultOrEmpty
        || !RequireAnyPermissions.IsDefaultOrEmpty
        || !UnresolvedRequireAllPaths.IsDefaultOrEmpty
        || !UnresolvedRequireAnyPaths.IsDefaultOrEmpty;

    /// <summary>Whether this declaration says anything at all about the operation.</summary>
    public bool HasDeclaration => HasAuthorization || DeclaredDto is not null;
}
