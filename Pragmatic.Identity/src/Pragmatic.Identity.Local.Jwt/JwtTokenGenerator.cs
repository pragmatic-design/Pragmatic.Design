using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Services;

namespace Pragmatic.Identity.Local.Jwt;

/// <summary>
///     Generates JWT tokens from a <see cref="LoginResult" />.
///     Embeds identity claims (sub, name, tenant, roles, permissions) in the token.
/// </summary>
/// <remarks>
///     The <see cref="IAccessTokenIssuer" /> <c>UseJwtAuthentication</c> registers: the package's
///     <see cref="SignInUser" /> signs its tokens here, and a module depends on the contract, not on this
///     class.
/// </remarks>
public sealed class JwtTokenGenerator(IOptions<JwtOptions> options) : IAccessTokenIssuer
{
    /// <inheritdoc />
    public AccessToken Issue(SignInClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        return Generate(claims.Subject, claims.DisplayName, claims.TenantId, claims.Roles, claims.Permissions,
            claims.SecurityStamp, claims.ExternalIdentityKey);
    }

    /// <summary>
    ///     The claim the display name is written to, and the one <c>UseJwtAuthentication</c> tells both
    ///     the validator and <see cref="IdentityOptions.DisplayNameClaimType" /> to read.
    /// </summary>
    /// <remarks>
    ///     One name in one place. JwtBearer renames <c>sub</c> and <c>role</c> on the way in and leaves
    ///     <c>name</c> as it is, so the writer and the reader have to agree on this one explicitly —
    ///     otherwise every bearer caller has a null display name.
    /// </remarks>
    internal const string NameClaimType = "name";

    // JwtSecurityTokenHandler is thread-safe for writing and stateless across calls,
    // so a single shared instance avoids a per-token allocation.
    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    /// <summary>
    ///     Generates a JWT token for the given login result and optional claims.
    /// </summary>
    /// <param name="subject">The user's external identity key (e.g., "local|user@example.com").</param>
    /// <param name="displayName">Optional display name.</param>
    /// <param name="tenantId">Optional tenant identifier.</param>
    /// <param name="roles">Optional role claims.</param>
    /// <param name="permissions">Optional permission claims.</param>
    /// <param name="securityStamp">
    ///     Optional per-identity security stamp. When supplied it is embedded as the <c>sstamp</c> claim
    ///     and re-validated on each request; rotating the stamp (on password change/reset) invalidates the token.
    /// </param>
    /// <param name="externalIdentityKey">
    ///     Optional. The key correlating this principal with its stored identity record, embedded as
    ///     the <c>pragmatic_eid</c> claim. Supply it whenever the token's issuer is not the identity
    ///     provider — a locally signed-in user is that case — so the reader does not have to recompose
    ///     the key from <c>iss</c> and get a different one.
    /// </param>
    /// <returns>An <see cref="AccessToken" /> carrying the token and its expiration.</returns>
    public AccessToken Generate(
        string subject,
        string? displayName = null,
        string? tenantId = null,
        IEnumerable<string>? roles = null,
        IEnumerable<string>? permissions = null,
        string? securityStamp = null,
        string? externalIdentityKey = null)
    {
        var opts = options.Value;
        if (Encoding.UTF8.GetByteCount(opts.SigningKey) < 32)
            throw new InvalidOperationException(
                "JWT SigningKey must be at least 32 bytes (256 bits) for HMAC-SHA256. " +
                "Use IValidateOptions<JwtOptions> or UseJwtAuthentication() to enforce this at startup.");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        if (externalIdentityKey is not null)
            claims.Add(new Claim(ExternalIdentityKey.ClaimType, externalIdentityKey));

        if (displayName is not null)
            claims.Add(new Claim(NameClaimType, displayName));

        if (tenantId is not null)
            claims.Add(new Claim("tenant_id", tenantId));

        if (!string.IsNullOrEmpty(securityStamp))
            claims.Add(new Claim("sstamp", securityStamp));

        if (roles is not null)
            foreach (var role in roles)
                claims.Add(new Claim("role", role));

        if (permissions is not null)
            foreach (var perm in permissions)
                claims.Add(new Claim("permission", perm));

        var expiresAt = DateTimeOffset.UtcNow.Add(opts.TokenExpiration);

        var token = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var tokenString = TokenHandler.WriteToken(token);
        return new AccessToken(tokenString, expiresAt);
    }

    /// <summary>
    ///     Generates a JWT token from a <see cref="LoginResult" />.
    /// </summary>
    /// <remarks>
    ///     <paramref name="loginResult" /> already carries the external identity key, so the token
    ///     carries it as its own claim rather than leaving it to be recomposed on the other side.
    ///     Recomposing would use the token's <c>iss</c> — the application that signed it — where the
    ///     stored key names the identity provider, and the two keys would never match.
    /// </remarks>
    public AccessToken Generate(LoginResult loginResult, string? displayName = null,
        string? tenantId = null, IEnumerable<string>? roles = null,
        IEnumerable<string>? permissions = null, string? securityStamp = null)
        => Generate(loginResult.ExternalIdentityKey, displayName, tenantId, roles, permissions, securityStamp,
            externalIdentityKey: loginResult.ExternalIdentityKey);
}
