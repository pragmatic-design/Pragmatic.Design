using System;
using System.Collections.Generic;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Delegation;

/// <summary>
///     Reads the actor's authority off the session's own claims — the shape a token-exchanged
///     delegation produces, where the issuer put both parties in one token.
/// </summary>
/// <remarks>
///     <para>
///         The default for the token door, and only for it. An application whose actors are jobs or
///         agents holds their authority somewhere else — a service-account store, a grant table — and
///         registers its own resolver.
///     </para>
///     <para>
///         Absent claims mean <b>no authority</b>, not full authority. Under
///         <see cref="DelegationPolicy.Intersection" /> that makes the delegation powerless rather
///         than unlimited, which is the correct direction to fail: a missing claim is an issuer that
///         did not say, and "did not say" must never read as "everything".
///     </para>
/// </remarks>
public sealed class ClaimsActorAuthorityResolver(ICurrentUser currentUser) : IActorAuthorityResolver
{
    /// <summary>Claim carrying the actor's own permissions, comma-separated.</summary>
    public const string ActorPermissionsClaim = DelegationClaims.ActorPermissions;

    /// <summary>Claim carrying the permissions the grant names, comma-separated.</summary>
    public const string GrantPermissionsClaim = DelegationClaims.GrantPermissions;

    /// <inheritdoc />
    public IReadOnlySet<string> ActorPermissions(IDelegationContext delegation)
        => Read(ActorPermissionsClaim);

    /// <inheritdoc />
    public IReadOnlySet<string> GrantedPermissions(IDelegationContext delegation)
        => Read(GrantPermissionsClaim);

    private IReadOnlySet<string> Read(string claimType)
    {
        if (!currentUser.Claims.TryGetValue(claimType, out var values) || values.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
            foreach (var permission in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                result.Add(permission);

        return result;
    }
}
