using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Specification.Models;

/// <summary>
///     A <c>static</c> member that returns a <c>Specification&lt;TEntity&gt;</c>, wherever it is
///     declared.
/// </summary>
/// <remarks>
///     Recognised <b>by type, not by an attribute</b>, which is the precedent
///     <c>QuerySpecificationModel</c> set deliberately: the type already says what the member is, and a
///     marker would be a second way to say the same thing.
/// </remarks>
internal sealed record DeclaredSpecificationModel
{
    /// <summary>The entity the specification filters, fully qualified with <c>global::</c>.</summary>
    public required string EntityFullTypeName { get; init; }

    /// <summary>The entity's simple name — it names the generated class.</summary>
    public required string EntityShortName { get; init; }

    /// <summary>The namespace of the declaring type: the extensions are emitted there.</summary>
    public required string Namespace { get; init; }

    /// <summary>The declaring type, fully qualified — the generated call names it.</summary>
    public required string ContainerFullTypeName { get; init; }

    /// <summary>The member's own name, which the extensions carry.</summary>
    public required string MemberName { get; init; }

    /// <summary>A property has no argument list; a method may have one.</summary>
    public required bool IsProperty { get; init; }

    /// <summary>
    ///     <c>true</c> when both the container and the member are public, which decides whether the
    ///     extensions can be public without <c>CS0050</c>.
    /// </summary>
    public required bool IsPubliclyVisible { get; init; }

    /// <summary>The member's parameters, which become the extensions' parameters after the receiver.</summary>
    public EquatableArray<SpecificationParameter> Parameters { get; init; } =
        EquatableArray<SpecificationParameter>.Empty;

    /// <summary>Everything needed to name the entity and the member is present.</summary>
    public bool IsValid =>
        !string.IsNullOrEmpty(EntityFullTypeName)
        && !string.IsNullOrEmpty(EntityShortName)
        && !string.IsNullOrEmpty(ContainerFullTypeName)
        && !string.IsNullOrEmpty(MemberName);
}
