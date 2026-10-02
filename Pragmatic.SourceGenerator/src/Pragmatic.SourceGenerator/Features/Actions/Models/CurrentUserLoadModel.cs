using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     <c>[LoadCurrentUser]</c> on an action or a mutation: the field the invoker fills with the signed-in
///     user's entity, and the entity once the pipeline has brought it.
/// </summary>
/// <remarks>
///     Read in two stages, like a <c>[FromCurrentUser]</c> binding: the transform sees the attribute, and
///     the user entity — another declaration of the compilation — arrives through the pipeline.
/// </remarks>
internal sealed record CurrentUserLoadModel
{
    /// <summary>The field's name as the author overrode it, or null for <c>_current{User}</c>.</summary>
    public string? FieldNameOverride { get; init; }

    /// <summary>The user entity, <c>global::</c>-qualified, once resolved.</summary>
    public string? UserTypeFullName { get; init; }

    /// <summary>The user entity's simple name, for the field's default name and the <c>NotFoundError</c>.</summary>
    public string? UserTypeName { get; init; }

    /// <summary>The generated <c>{User}Resolver</c>, <c>global::</c>-qualified, once resolved.</summary>
    public string? ResolverTypeFullName { get; init; }

    /// <summary>Why it cannot be generated — the tail of PRAG0451 — or null when it can.</summary>
    public string? Problem { get; init; }

    /// <summary>Where the attribute is written, for PRAG0451.</summary>
    public LocationInfo? Location { get; init; }

    /// <summary>The field the invoker fills.</summary>
    public string FieldName => FieldNameOverride ?? $"_current{UserTypeName}";

    /// <summary>Whether the invoker loads it: the entity resolved and nothing in the way.</summary>
    public bool IsRendered => Problem is null && ResolverTypeFullName is not null;
}
