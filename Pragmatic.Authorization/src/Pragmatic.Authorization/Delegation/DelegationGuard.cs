using System;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Delegation;

/// <summary>
///     The checks a delegation has to survive before its authority is composed at all.
/// </summary>
/// <remarks>
///     Separate from <see cref="DelegatedUserAuthorization" /> on purpose: that one composes, this one
///     admits. A refused delegation must not reach the composition, because every policy there starts
///     from the subject's authority and would hand some of it over.
/// </remarks>
public static class DelegationGuard
{
    /// <summary>Why a delegation was refused, or <see cref="DelegationRefusal.None" />.</summary>
    public static DelegationRefusal Check(
        IDelegationContext delegation,
        string? subjectTenantId,
        string? actorTenantId,
        bool allowCrossTenant,
        int maxChainDepth)
    {
        ArgumentNullException.ThrowIfNull(delegation);

        if (delegation.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            return DelegationRefusal.Expired;

        // Depth counts the actors, and the current one is not always in the chain: a single hop
        // carries an empty chain. Counting it as at least one keeps a cap of 1 meaning "one actor".
        var depth = Math.Max(delegation.Chain.Count, 1);
        if (depth > maxChainDepth)
            return DelegationRefusal.ChainTooDeep;

        // A boundary of isolation, so it fails closed. Nothing defined which tenant a delegated
        // session belonged to before this, which means a cross-tenant delegation would silently have
        // picked one.
        // A null actor tenant means "same tenant as the subject" — the single-tenant case, and the
        // honest answer from a resolver with no notion of tenancy. Comparing it literally would refuse
        // every delegation in every single-tenant application.
        if (!allowCrossTenant
            && actorTenantId is { Length: > 0 }
            && !string.Equals(subjectTenantId, actorTenantId, StringComparison.Ordinal))
            return DelegationRefusal.CrossTenant;

        return DelegationRefusal.None;
    }
}

/// <summary>Why a delegation was not admitted.</summary>
public enum DelegationRefusal
{
    /// <summary>Admitted.</summary>
    None = 0,

    /// <summary>Its expiry has passed.</summary>
    Expired = 1,

    /// <summary>More actors in the chain than <c>MaxDelegationChainDepth</c> allows.</summary>
    ChainTooDeep = 2,

    /// <summary>Actor and subject are in different tenants, and that is not enabled.</summary>
    CrossTenant = 3,
}
