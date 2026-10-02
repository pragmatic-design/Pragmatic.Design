using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Patch.Models;

/// <summary>Immutable model for a patch DTO, used for incremental generator caching.</summary>
internal sealed record PatchModel
{
    public string Namespace { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string Accessibility { get; init; } = "public";

    /// <summary>Fully-qualified entity type name (with global::).</summary>
    public string EntityFullName { get; init; } = "";

    public string EntityName { get; init; } = "";
    public EquatableArray<PatchPropertyModel> Properties { get; init; } = EquatableArray<PatchPropertyModel>.Empty;

    public bool IsValid => !string.IsNullOrEmpty(TypeName) && !string.IsNullOrEmpty(EntityFullName) && Reason is null;
    public InvalidReason? Reason { get; init; }

    /// <summary>
    ///     Names given to <c>[PatchIgnore]</c> that match no property on the entity.
    /// </summary>
    /// <remarks>
    ///     Reported rather than ignored: a typo restores the property the author wrote the attribute
    ///     to protect, and the patch goes on offering a field the endpoint will refuse.
    /// </remarks>
    public EquatableArray<string> UnmatchedIgnores { get; init; } = EquatableArray<string>.Empty;
}
