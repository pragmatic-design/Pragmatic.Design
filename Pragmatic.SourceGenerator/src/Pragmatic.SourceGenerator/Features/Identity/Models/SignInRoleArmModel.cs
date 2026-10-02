using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Identity.Models;

/// <summary>One member of a <c>[SignsInAs]</c> enum and the role it signs in as — none, for a member that declares none.</summary>
internal sealed record SignInRoleArmModel
{
    /// <summary>The member's name.</summary>
    public required string Member { get; init; }

    /// <summary>The role class, <c>global::</c>-qualified; null for a member without <c>[SignsInAs]</c>.</summary>
    public string? RoleFullName { get; init; }

    /// <summary>Where the member is declared — where a missing role is reported.</summary>
    public LocationInfo? Location { get; init; }
}
