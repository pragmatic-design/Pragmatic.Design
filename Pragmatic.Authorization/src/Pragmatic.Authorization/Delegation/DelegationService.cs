using System;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Delegation;

/// <summary>
///     Opens a <see cref="DelegationScope" /> on behalf of whoever is currently executing.
/// </summary>
/// <remarks>
///     The actor is the caller's own identity, taken from <see cref="ICurrentUser" /> and not passed
///     in: an API where the caller names both parties lets it name an actor it is not, and the whole
///     point of composing authority is that the actor is a fact rather than a claim.
/// </remarks>
public sealed class DelegationService(ICurrentUser currentUser) : IDelegationService
{
    /// <inheritdoc />
    public IDisposable ActAs(
        string subjectId,
        string? purpose = null,
        DelegationPolicy policy = DelegationPolicy.Intersection,
        ActorKind actorKind = ActorKind.Service,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        // An unauthenticated caller has no authority to lend, and Intersection with nothing is
        // nothing — but GrantScoped would happily hand over whatever the grant names. Refusing here
        // keeps "who is acting" from being answerable with "nobody".
        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            throw new InvalidOperationException(
                "Cannot act on behalf of anyone from an unauthenticated context: there would be no actor. " +
                "Open the scope from code that has an identity, or give the process one.");

        return DelegationScope.Begin(
            subjectId,
            actorId: currentUser.Id,
            actorKind: actorKind,
            policy: policy,
            purpose: purpose,
            expiresAt: expiresAt);
    }
}
