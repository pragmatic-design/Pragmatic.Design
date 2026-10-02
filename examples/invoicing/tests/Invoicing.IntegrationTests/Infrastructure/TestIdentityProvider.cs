using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Invoicing.IntegrationTests.Infrastructure;

/// <summary>
///     The company's identity provider, for the tests: it signs tokens the real handler validates.
/// </summary>
/// <remarks>
///     <para>
///         No container and no network. The handler takes a static configuration when one is set, so the
///         discovery document is replaced by the signing key and everything else — issuer, audience and
///         lifetime validation, the role claim, the claims transformer — runs exactly as it does in
///         production. It is how the framework tests this itself
///         (<c>Pragmatic.Identity.Oidc.Tests/TheTokenReachesTheCurrentUserTests.cs</c>).
///     </para>
///     <para>
///         The roles go in as one claim holding a JSON array, which is the shape a provider like Keycloak
///         sends; the single-claim-per-role shape is the other one the framework accepts.
///     </para>
/// </remarks>
public static class TestIdentityProvider
{
    public const string Authority = "https://idp.invoicing.test";

    public const string Audience = "invoicing-api";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("invoicing-test-signing-key-32-chars!"));

    /// <summary>Replaces the provider's discovery document with the key this class signs with.</summary>
    public static void Configure(IServiceCollection services) =>
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Authority };
            configuration.SigningKeys.Add(SigningKey);
            jwt.Configuration = configuration;
        });

    /// <summary>A token for <paramref name="user" />, valid unless something is asked to be wrong.</summary>
    /// <param name="user">Who is calling.</param>
    /// <param name="tenant">
    ///     The company they belong to, as the <c>tenant_id</c> claim. Null mints a token that carries none —
    ///     which is what an operation that runs before any tenant exists is called with, and what every
    ///     other route refuses.
    /// </param>
    /// <param name="audience">Another audience than this application's, to be refused.</param>
    /// <param name="issuer">Another issuer than the provider, to be refused.</param>
    public static string Token(TestUser user, string? tenant = null, string? audience = null, string? issuer = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.Name,
        };

        if (user.Roles.Length > 0)
            claims["roles"] = user.Roles;

        if (tenant is not null)
            claims["tenant_id"] = tenant;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Authority,
            Audience = audience ?? Audience,
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
