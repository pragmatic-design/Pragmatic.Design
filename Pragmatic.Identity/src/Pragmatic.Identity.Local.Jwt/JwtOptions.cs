namespace Pragmatic.Identity.Local.Jwt;

/// <summary>
///     Configuration for JWT token generation and validation.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    ///     Symmetric signing key for HMAC-SHA256. Must be at least 32 bytes (256 bits).
    /// </summary>
    /// <remarks>
    ///     Length is not the same as entropy: a 32-byte key made of a repeated word or a handful of
    ///     characters is trivially guessable. Generate a random key rather than typing a passphrase, e.g.
    ///     <c>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))</c>. Startup validation
    ///     (<see cref="JwtOptionsValidator" />) rejects both under-length and obviously low-entropy keys.
    /// </remarks>
    public required string SigningKey { get; set; }

    /// <summary>
    ///     Token issuer (iss claim).
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    ///     Token audience (aud claim). If null, audience validation is skipped.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    ///     Token expiration time. Default: 1 hour.
    /// </summary>
    /// <remarks>
    ///     This value bounds revocation latency for the <b>stateless</b> authorization model
    ///     (<c>AuthorizationOptions.TrustPermissionClaims = true</c>, baking <c>role</c>/<c>permission</c>
    ///     claims into the token): a revoked grant stays valid until the token expires, so keep the TTL
    ///     short and rotate the security stamp on authorization changes. The default server-side model
    ///     (<c>TrustPermissionClaims = false</c>) resolves permissions per request and revokes immediately
    ///     via cache invalidation, independent of this TTL.
    /// </remarks>
    public TimeSpan TokenExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     Clock skew tolerance for token validation. Default: 1 minute.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     When <c>true</c> (the default), a validated token without an <c>sstamp</c> (security stamp)
    ///     claim is rejected — fail closed. A token with one is always checked against
    ///     <c>ILocalIdentityStore</c>, which must then be registered.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The stamp is revocation: a password change or reset rotates it, and every token issued
    ///         before stops being accepted. A token without one cannot be revoked.
    ///     </para>
    ///     <para>
    ///         Set to <c>false</c> in two cases, and only these. <b>(a) A migration window</b>: stamp-less
    ///         tokens minted before stamps existed must still be accepted until they expire; the lenient
    ///         path is unsafe once every live token carries a stamp. <b>(b) A host whose tokens come from an
    ///         issuer that holds the credentials and owns their revocation</b> — an external identity
    ///         provider or a separate token service: this host stores no password, so it has no stamp to
    ///         rotate, and the token's lifetime is the bound on how long a revoked session keeps working.
    ///     </para>
    /// </remarks>
    public bool RequireSecurityStamp { get; set; } = true;
}
