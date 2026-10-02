using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Delegation;

/// <summary>
///     An <see cref="AsyncLocal{T}" /> delegation for code that has no HTTP request behind it —
///     background jobs, event handlers, agent runners, seeds and tests.
/// </summary>
/// <remarks>
///     <para>
///         Same shape as <c>TenantScope</c>, and for the same reason: an ambient value that flows
///         through async continuations. Its remarks record the trap this one has to avoid — the tenant
///         scope existed for a while and nothing read it, so the documented background-job story was
///         inert. Here the reader is <see cref="AmbientDelegatedUser" />, and it is registered in the
///         same call that composes authority.
///     </para>
///     <para>
///         This is the alternative to running background work as a full-permission system identity.
///         The scope says <em>for whom</em> and <em>with what policy</em>, and everything downstream —
///         permission checks, row ownership, filters, cache keys, audit — follows from that.
///     </para>
/// </remarks>
public static class DelegationScope
{
    private static readonly AsyncLocal<IDelegationContext?> Current = new();

    /// <summary>The delegation in force for the current async flow, if any.</summary>
    public static IDelegationContext? Value => Current.Value;

    /// <summary>
    ///     Opens a delegation for the current async flow. Dispose to restore the previous one.
    /// </summary>
    /// <remarks>
    ///     Opening one inside another <b>extends the chain</b> rather than replacing it, so an agent
    ///     that starts an agent is visible as two hops and
    ///     <c>AuthorizationOptions.MaxDelegationChainDepth</c> can actually refuse it. Replacing would
    ///     have made the cap unreachable.
    /// </remarks>
    public static IDisposable Begin(
        string subjectId,
        string actorId,
        ActorKind actorKind = ActorKind.Service,
        DelegationPolicy policy = DelegationPolicy.Intersection,
        string? purpose = null,
        string? grantId = null,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var previous = Current.Value;
        var chain = previous is null
            ? new List<string>()
            : [.. previous.Chain.DefaultIfEmpty(previous.ActorId).Distinct(StringComparer.Ordinal)];

        if (previous is not null && !chain.Contains(actorId, StringComparer.Ordinal))
            chain.Add(actorId);

        Current.Value = new ScopedDelegation(
            subjectId, actorId, actorKind, policy, purpose, grantId, expiresAt, chain);

        return new Restore(previous);
    }

    private sealed class Restore(IDelegationContext? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }

    private sealed record ScopedDelegation(
        string SubjectId,
        string ActorId,
        ActorKind ActorKind,
        DelegationPolicy Policy,
        string? Purpose,
        string? GrantId,
        DateTimeOffset? ExpiresAt,
        IReadOnlyList<string> Chain) : IDelegationContext;
}
