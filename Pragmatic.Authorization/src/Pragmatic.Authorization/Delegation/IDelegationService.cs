using System;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Delegation;

/// <summary>
///     Starts a delegation from code.
/// </summary>
/// <remarks>
///     Injectable rather than a static call on <see cref="DelegationScope" />, so a test can substitute
///     it and an application can wrap it — with a grant check, an audit entry, a notification to the
///     subject — without every caller having to remember to do those things.
/// </remarks>
public interface IDelegationService
{
    /// <summary>
    ///     Acts for <paramref name="subjectId" /> until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A background job wants <see cref="DelegationPolicy.GrantScoped" /> with the operations it
    ///         needs. The default is <see cref="DelegationPolicy.Intersection" />, which for a job whose
    ///         identity holds no user-level permissions grants nothing at all — safe, and useless.
    ///     </para>
    ///     <para>
    ///         ⚠️ The handle must not outlive the unit of work that opened it. It is an
    ///         <see cref="AsyncLocal{T}" />, so it flows into everything awaited inside the
    ///         <c>using</c> — and into nothing after it, provided there is a <c>using</c>.
    ///     </para>
    /// </remarks>
    IDisposable ActAs(
        string subjectId,
        string? purpose = null,
        DelegationPolicy policy = DelegationPolicy.Intersection,
        ActorKind actorKind = ActorKind.Service,
        DateTimeOffset? expiresAt = null);
}
