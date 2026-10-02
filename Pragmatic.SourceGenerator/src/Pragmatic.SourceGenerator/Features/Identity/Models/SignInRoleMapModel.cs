using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Identity.Models;

/// <summary>
///     An enum some member of which declares <c>[SignsInAs&lt;TRole&gt;]</c>: the role each member signs in as, and the
///     members that declare none.
/// </summary>
internal sealed record SignInRoleMapModel
{
    /// <summary>The enum's name.</summary>
    public required string EnumName { get; init; }

    /// <summary>The enum's namespace; empty for the global one.</summary>
    public required string Namespace { get; init; }

    /// <summary>The enum's full name, <c>global::</c>-qualified.</summary>
    public required string EnumFullName { get; init; }

    /// <summary>Whether the enum is visible outside its assembly — the mapping is as visible as it.</summary>
    public bool IsPublic { get; init; }

    /// <summary>Each member that declares a role, in declaration order.</summary>
    public EquatableArray<SignInRoleArmModel> Arms { get; init; } = EquatableArray<SignInRoleArmModel>.Empty;

    /// <summary>The members that declare none (PRAG1014).</summary>
    public EquatableArray<SignInRoleArmModel> Unmapped { get; init; } = EquatableArray<SignInRoleArmModel>.Empty;
}
