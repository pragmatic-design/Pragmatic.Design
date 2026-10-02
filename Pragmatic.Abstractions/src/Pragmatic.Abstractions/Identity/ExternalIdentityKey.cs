namespace Pragmatic.Identity;

/// <summary>
///     The one definition of the external identity key — the value that correlates an authenticated
///     principal with the identity record stored for it.
/// </summary>
/// <remarks>
///     <para>
///         The format is <c>{issuer}|{subject}</c>, with both halves percent-escaped. The escaping is
///         not decoration: without it an issuer <c>a|b</c> with subject <c>c</c> produces the same key
///         as issuer <c>a</c> with subject <c>b|c</c>, so two different principals collide on one
///         identity record.
///     </para>
///     <para>
///         The format is defined here once so that there is one spelling of it. Two spellings of the
///         format are not a cosmetic difference: a key written by one and looked up by the other does
///         not match, and the resolver answers "no such user" without any error.
///     </para>
/// </remarks>
public static class ExternalIdentityKey
{
    /// <summary>
    ///     The claim that carries a ready-made key, for a token whose issuer is not the identity
    ///     provider.
    /// </summary>
    /// <remarks>
    ///     An application that authenticates a user locally and then mints its own JWT has two
    ///     different issuers in play: the token's <c>iss</c>, which says who signed it, and the
    ///     identity provider, which says where the identity came from. Composing the key from
    ///     <c>iss</c> would produce a key naming the wrong one. A token that carries this claim says
    ///     the key outright and needs no composing.
    /// </remarks>
    public const string ClaimType = "pragmatic_eid";

    /// <summary>The separator between the two halves. Present in neither half after escaping.</summary>
    public const char Separator = '|';

    /// <summary>
    ///     Composes the key for an identity provider and a subject at that provider.
    /// </summary>
    /// <param name="issuer">The identity provider (an issuer URI, or a scheme name such as "local").</param>
    /// <param name="subject">The subject's identifier at that provider.</param>
    /// <returns>The key, or null when either half is missing — a half key correlates nothing.</returns>
    public static string? Compose(string? issuer, string? subject)
        => string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(subject)
            ? null
            : $"{Uri.EscapeDataString(issuer!)}{Separator}{Uri.EscapeDataString(subject!)}";
}
