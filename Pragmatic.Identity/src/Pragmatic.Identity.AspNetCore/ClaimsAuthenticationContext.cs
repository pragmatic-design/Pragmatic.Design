using System.Security.Claims;

namespace Pragmatic.Identity;

/// <summary>
///     Implements <see cref="IAuthenticationContext" /> by reading from a <see cref="ClaimsPrincipal" />.
/// </summary>
internal sealed class ClaimsAuthenticationContext(ClaimsPrincipal? principal, IdentityOptions options)
    : IAuthenticationContext
{
    /// <inheritdoc />
    public string? Scheme => principal?.Identity?.AuthenticationType;

    /// <inheritdoc />
    public string? Protocol => principal?.FindFirst("acr")?.Value;

    /// <inheritdoc />
    public string? Issuer => principal?.FindFirst("iss")?.Value;

    /// <inheritdoc />
    public string? Subject => principal?.FindFirst(options.UserIdClaimType)?.Value;

    /// <inheritdoc />
    /// <remarks>
    ///     Matches an <c>amr</c> (Authentication Methods References, RFC 8176) value EXACTLY against
    ///     <c>mfa</c>, case-insensitively, across every <c>amr</c> claim. A substring test would wrongly
    ///     accept unrelated method names such as <c>smfa</c>; equality avoids that elevation.
    /// </remarks>
    public bool IsMfaAuthenticated =>
        principal is not null
        && principal.FindAll("amr").Any(c => string.Equals(c.Value, "mfa", StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public DateTimeOffset? AuthenticatedAt => ParseEpoch(principal?.FindFirst("auth_time")?.Value);

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAt => ParseEpoch(principal?.FindFirst("exp")?.Value);

    /// <inheritdoc />
    /// <remarks>
    ///     Each component is percent-escaped before joining with <c>|</c> so that crafted issuer/subject
    ///     values containing the separator cannot collide (e.g. issuer <c>a|b</c>+subject <c>c</c> would
    ///     otherwise produce the same key as issuer <c>a</c>+subject <c>b|c</c>).
    ///     <para>
    ///         The claim first, composing second. A token minted by the application after a local
    ///         sign-in has an <c>iss</c> saying who signed it, not where the identity came from;
    ///         composing from it would name the wrong provider and produce a key matching no stored
    ///         identity. A pure OIDC token carries no such claim, and there the two are the same
    ///         thing, so composing is right.
    ///     </para>
    /// </remarks>
    public string? ExternalIdentityKey
        => principal?.FindFirst(Pragmatic.Identity.ExternalIdentityKey.ClaimType)?.Value
           ?? Pragmatic.Identity.ExternalIdentityKey.Compose(Issuer, Subject);

    private static DateTimeOffset? ParseEpoch(string? value)
    {
        if (value is null || !long.TryParse(value, out var epoch))
            return null;

        return DateTimeOffset.FromUnixTimeSeconds(epoch);
    }
}
