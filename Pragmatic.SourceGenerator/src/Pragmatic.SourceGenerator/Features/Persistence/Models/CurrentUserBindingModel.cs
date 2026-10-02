using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One <c>[FromCurrentUser]</c> property of a query: what the invoker writes into it, and whether it
///     can.
/// </summary>
/// <remarks>
///     Read in two stages. The query transform records what the property declares; the invoker pass,
///     which receives the <c>[PragmaticUser]</c> entity through the pipeline, fills in the user side.
///     The entity is another declaration in the compilation, so a per-query transform could only find it
///     by searching, and its result would then depend on syntax the incremental cache does not track.
/// </remarks>
internal sealed record CurrentUserBindingModel
{
    /// <summary>The query property the invoker writes.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The property's type, fully qualified, without a nullable reference annotation.</summary>
    public required string PropertyTypeFullName { get; init; }

    /// <summary>Whether the property is a <c>string</c>, which is what the member-less form binds.</summary>
    public bool PropertyIsString { get; init; }

    /// <summary>The member of the user entity to bind, or null for <c>ICurrentUser.Id</c>.</summary>
    public string? Member { get; init; }

    /// <summary>
    ///     The type the author's <c>nameof</c> named the member of, when it resolved — to tell a member
    ///     of the user entity from a member of the same name on another type.
    /// </summary>
    public string? MemberQualifier { get; init; }

    /// <summary>Whether an argument was written and no member name could be read from it.</summary>
    public bool MemberIsUnreadable { get; init; }

    /// <summary>Whether the setter is private and not <c>init</c> — the one form only the invoker reaches.</summary>
    public bool OnlyTheInvokerSetsIt { get; init; }

    /// <summary>Whether the invoker can assign it at all: a setter that is not <c>init</c>.</summary>
    public bool IsAssignable { get; init; }

    /// <summary>Where the property is declared, for PRAG0730 and PRAG0731.</summary>
    public LocationInfo? Location { get; init; }

    /// <summary>The user entity, <c>global::</c>-qualified, once resolved; null for the member-less form.</summary>
    public string? UserTypeFullName { get; init; }

    /// <summary>The user entity's simple name, for the <c>NotFoundError</c>.</summary>
    public string? UserTypeName { get; init; }

    /// <summary>The generated <c>{User}Resolver</c>, <c>global::</c>-qualified, once resolved.</summary>
    public string? ResolverTypeFullName { get; init; }

    /// <summary>Why the binding cannot be generated — the tail of PRAG0731 — or null when it can.</summary>
    public string? Problem { get; init; }

    /// <summary>Whether the invoker writes the binding: nothing prevents it, and the assignment compiles.</summary>
    public bool IsRendered => Problem is null && IsAssignable;
}
