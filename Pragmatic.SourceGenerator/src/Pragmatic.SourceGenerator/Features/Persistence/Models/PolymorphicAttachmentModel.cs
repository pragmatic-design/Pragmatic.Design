using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for generating polymorphic attachment support — OwnerType/OwnerId columns,
///     indices, and ForOwner&lt;T&gt;() query extension.
/// </summary>
internal sealed record PolymorphicAttachmentModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }

    /// <summary>Owner types that can own this attachment (from [Attachable&lt;T&gt;]).</summary>
    public EquatableArray<OwnerTypeModel> OwnerTypes { get; init; } = EquatableArray<OwnerTypeModel>.Empty;

    public bool IsValid => !string.IsNullOrEmpty(TypeName);
    public bool HasOwners => !OwnerTypes.IsDefaultOrEmpty;
}

internal sealed record OwnerTypeModel
{
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }

    /// <summary>Namespace of the owner type (for generating virtual nav in correct namespace).</summary>
    public string? Namespace { get; init; }

    /// <summary>ID type of the owner entity (e.g. "System.Guid").</summary>
    public string IdType { get; init; } = "System.Guid";
}
