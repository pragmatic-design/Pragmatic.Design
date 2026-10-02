namespace Pragmatic.Messaging;

/// <summary>
///     What a consumer learns when it tries to claim a message id.
/// </summary>
/// <remarks>
///     ⚠️ Three answers and not two. A test-and-set store answers "was it seen?" — which conflates
///     <b>somebody started</b> with <b>somebody finished</b>. A consumer that claims before handling and
///     then dies leaves the first, and a redelivery that read it as the second would drop the message
///     without any handler ever running.
/// </remarks>
public enum MessageClaim
{
    /// <summary>Nobody held it, or the previous holder's lease had run out. Handle it.</summary>
    Claimed = 0,

    /// <summary>It has already been handled to completion. Drop this copy.</summary>
    AlreadyHandled = 1,

    /// <summary>
    ///     Somebody else is handling it right now, within their lease. Drop this copy — running it
    ///     here as well is the double handling the claim exists to prevent.
    /// </summary>
    HeldByAnother = 2,
}
