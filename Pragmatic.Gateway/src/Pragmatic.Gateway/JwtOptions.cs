namespace Pragmatic.Gateway;

/// <summary>
///     JWT bearer authentication configuration.
///     <para>
///         <b>Security note</b>: <see cref="SigningKey" /> is a plain string that may appear in
///         diagnostic dumps and config snapshots. Prefer storing it via a secrets manager
///         (Azure Key Vault, HashiCorp Vault, or an environment variable) and never commit it
///         to source control or appsettings.json.
///     </para>
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    ///     Expected token issuer (<c>iss</c> claim). When set, <c>ValidateIssuer</c> is
    ///     automatically enabled. <b>Strongly recommended in production.</b>
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    ///     Expected token audience (<c>aud</c> claim). When set, <c>ValidateAudience</c> is
    ///     automatically enabled. <b>Strongly recommended in production.</b>
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    ///     HMAC symmetric signing key (Base64 or UTF-8 string).
    ///     <b>Do not store in appsettings.json.</b> Use a secrets manager or env var.
    ///     Mutually exclusive with <see cref="JwksUrl" />.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    ///     OIDC/JWKS metadata URL for asymmetric key validation (e.g.
    ///     <c>https://auth.example.com/.well-known/openid-configuration</c>).
    ///     Mutually exclusive with <see cref="SigningKey" />.
    /// </summary>
    public string? JwksUrl { get; set; }

    /// <summary>JWT claims to forward to downstream backends as request headers.</summary>
    public List<string> ForwardClaims { get; set; } = ["sub", "role"];
}
