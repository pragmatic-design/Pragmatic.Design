namespace Pragmatic.Http;

/// <summary>
///     The outcome of inspecting an outbound URL.
/// </summary>
/// <remarks>
///     Distinct reasons rather than a bool, because the caller's response differs: a refused scheme is a
///     mistake worth telling the user about, while an address that resolves internally is a probe worth
///     recording. Collapsing them would lose the difference between a typo and an attempt.
/// </remarks>
public enum OutboundUrlVerdict
{
    /// <summary>Nothing objectionable was found. See the guard's remarks on what that does not mean.</summary>
    Allowed = 0,

    /// <summary>Not parseable as an absolute URL.</summary>
    NotAnAbsoluteUrl = 1,

    /// <summary>Not https (or http, when explicitly permitted).</summary>
    SchemeNotAllowed = 2,

    /// <summary>
    ///     Credentials embedded in the URL. Refused because <c>https://real-host@attacker/</c> reads as
    ///     the real host to a person and resolves to the attacker's for a machine.
    /// </summary>
    CredentialsInUrl = 3,

    /// <summary>The host is, or resolves to, an address inside the application's own network.</summary>
    ResolvesToInternalAddress = 4,

    /// <summary>
    ///     The host could not be resolved. A refusal, not an inconclusive result: a host that cannot be
    ///     shown safe is not allowed through.
    /// </summary>
    HostCouldNotBeResolved = 5,
}
