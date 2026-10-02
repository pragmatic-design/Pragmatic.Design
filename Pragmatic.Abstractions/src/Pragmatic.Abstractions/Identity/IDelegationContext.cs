using System;
using System.Collections.Generic;

namespace Pragmatic.Identity;

/// <summary>
///     Present when someone is acting <em>on behalf of</em> someone else: an agent for the user who
///     started it, a support operator for a customer, a background job for the owner of a row.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ICurrentUser.Id" /> stays the <b>subject</b> — whoever the work is being done
///         for. That keeps everything that never heard of delegation behaving correctly: row
///         ownership is stamped with the subject, ownership and scope filters select the subject's
///         rows, culture and preferences are the subject's. Code that needs to know who is actually
///         executing asks <see cref="ActorId" />.
///     </para>
///     <para>
///         The effective authority is <b>not</b> the subject's: it is composed from subject and actor
///         according to <see cref="Policy" />, and every existing <c>[RequirePermission]</c>, filter
///         and resource policy inherits that composition without knowing it exists.
///     </para>
///     <para>
///         The vocabulary is RFC 8693 (OAuth 2.0 Token Exchange), where <c>act</c> carries the acting
///         party alongside <c>sub</c>. The framework already read <c>act_sub</c> before any of this
///         existed — and did nothing with it.
///     </para>
/// </remarks>
public interface IDelegationContext
{
    /// <summary>Who the work is being done for. Always equal to <see cref="ICurrentUser.Id" />.</summary>
    string SubjectId { get; }

    /// <summary>Who is actually acting.</summary>
    string ActorId { get; }

    /// <summary>What kind of party the actor is.</summary>
    ActorKind ActorKind { get; }

    /// <summary>How subject and actor authority combine.</summary>
    DelegationPolicy Policy { get; }

    /// <summary>
    ///     Why this delegation exists — "support ticket 4711", "run 92". Free text, and it belongs in
    ///     the audit trail: half the value of delegation is being able to answer "who, for whom, and
    ///     what for".
    /// </summary>
    string? Purpose { get; }

    /// <summary>The grant that authorised it, when one was involved.</summary>
    string? GrantId { get; }

    /// <summary>When it stops being valid, if it expires.</summary>
    DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    ///     The acting parties from the outermost inwards, when an actor delegated to another actor.
    ///     Empty for the ordinary single-hop case.
    /// </summary>
    /// <remarks>
    ///     Authority intersects along the whole chain, and the depth is capped: a chain without a
    ///     limit is a slow way back to full authority.
    /// </remarks>
    IReadOnlyList<string> Chain { get; }
}
