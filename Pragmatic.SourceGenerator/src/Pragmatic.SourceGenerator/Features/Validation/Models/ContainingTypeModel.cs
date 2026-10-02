namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     A type that encloses a validatable one, as the generated file has to reopen it.
/// </summary>
/// <remarks>
///     ⚠️ <see cref="IsPartial" /> is not a detail: the generated file declares the container again, and
///     C# refuses a second declaration of a type that was not written <c>partial</c>. Without the flag
///     the generator would emit source that cannot compile for a reason the author cannot see from the
///     error — as it would by dropping the nesting entirely.
/// </remarks>
internal sealed record ContainingTypeModel
{
    /// <summary>The container's simple name.</summary>
    public required string TypeName { get; init; }

    /// <summary>Its kind, as the reopened declaration must repeat it: class, struct, record, …</summary>
    public required string TypeKind { get; init; }

    /// <summary>Its accessibility, likewise repeated.</summary>
    public required string Accessibility { get; init; }

    /// <summary>Whether it can be reopened at all.</summary>
    public required bool IsPartial { get; init; }

    /// <summary>
    ///     Whether it is static, as the reopened declaration must say so too.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Omitting it on the second declaration is a conflicting-modifiers error, and a container
    ///     holding request types is very often <c>static</c>.
    /// </remarks>
    public required bool IsStatic { get; init; }
}
