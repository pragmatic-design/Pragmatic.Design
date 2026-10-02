using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Identity.Models;

/// <summary>
///     A custom permission declared in this assembly — <c>[assembly: Permission(…)]</c>, or a
///     <c>[RequirePermission("…", Description = "…")]</c> — whose constant goes into the one
///     <c>{Boundary}Permissions</c> class and whose entry goes into the permission registry.
/// </summary>
internal sealed record DeclaredPermissionModel
{
    /// <summary>The permission's value — <c>{boundary}.{resource}.{verb}</c>.</summary>
    public required string Value { get; init; }

    /// <summary>What holding it allows.</summary>
    public string? Description { get; init; }

    /// <summary>The group a role screen lists it under.</summary>
    public string? Category { get; init; }

    /// <summary>Where it is declared, as a diagnostic names it — <c>[assembly: Permission]</c>, <c>[RequirePermission] on X</c>.</summary>
    public required string Source { get; init; }

    public LocationInfo? Location { get; init; }
}
