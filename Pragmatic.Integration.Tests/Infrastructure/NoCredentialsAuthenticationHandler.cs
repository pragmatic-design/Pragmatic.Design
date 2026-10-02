using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Pragmatic.Integration.Tests.Infrastructure;

/// <summary>
///     An authentication scheme with no credentials of its own: a caller that sends nothing is
///     anonymous, and a challenge answers 401. A caller that sends <see cref="UserHeader" /> is that
///     user, with the roles listed in <see cref="RolesHeader" />.
/// </summary>
/// <remarks>
///     It stands in for a real identity provider when the question is what an endpoint lets through:
///     an anonymous caller, an authenticated one who fails a policy, and one who meets it.
/// </remarks>
public sealed class NoCredentialsAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "NoCredentials";

    /// <summary>The user name; its absence is an anonymous caller.</summary>
    public const string UserHeader = "X-Test-User";

    /// <summary>Comma-separated role claims for the user.</summary>
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrEmpty(user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };
        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
            claims.AddRange(roles.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
