using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Pragmatic.Composition;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Keycloak.Tests;

/// <summary>
///     A Keycloak token, validated by the handler this package registers, reaches the current user
///     with the name the identity carries.
/// </summary>
/// <remarks>
///     <para>
///         The validator names the user by <c>preferred_username</c>, so <c>User.Identity.Name</c> is the
///         username; <c>ICurrentUser.DisplayName</c> read <c>IdentityOptions.DisplayNameClaimType</c>,
///         the WS-* name URI, which JwtBearer never produces from a Keycloak token. Two components of
///         one request disagreed about who the caller is called, and the one Pragmatic reads said
///         nobody.
///     </para>
///     <para>
///         The realm's discovery document is replaced by a static configuration carrying the signing
///         key, so the handler validates for real without a Keycloak to ask.
///     </para>
/// </remarks>
public class TheTokenReachesTheCurrentUserTests
{
    private const string BaseUrl = "https://keycloak.example.com";
    private const string Realm = "myrealm";
    private const string Authority = BaseUrl + "/realms/" + Realm;
    private const string Audience = "my-api";

    private static readonly SymmetricSecurityKey Key =
        new(Encoding.UTF8.GetBytes("keycloak-round-trip-key-of-32-chars!"));

    [Fact]
    public async Task TheNameTheIdentityCarries_IsTheDisplayName()
    {
        var (id, displayName, identityName) = await AuthenticateAsync();

        displayName.Should().Be(identityName, "the name Pragmatic reads is the name ASP.NET Core reads");
        displayName.Should().Be("grace");
    }

    /// <summary>The control: the token is accepted and the subject arrives, today and after.</summary>
    [Fact]
    public async Task TheSubject_IsTheId()
    {
        var (id, _, _) = await AuthenticateAsync();

        id.Should().Be("kc-1");
    }

    private static async Task<(string Id, string? DisplayName, string? IdentityName)> AuthenticateAsync()
    {
        var builder = new FakeBuilder();
        builder.Services.AddLogging();

        // What the generated host registers, before the application's callback runs.
        builder.Services.AddPragmaticIdentity();
        builder.UseKeycloakAuthentication(k =>
        {
            k.BaseUrl = BaseUrl;
            k.Realm = Realm;
            k.Audience = Audience;
        });

        // The realm's discovery document, without a realm: the handler takes a static configuration
        // when one is set, and never goes to the network.
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Authority };
            configuration.SigningKeys.Add(Key);
            jwt.Configuration = configuration;
        });

        var provider = builder.Services.BuildServiceProvider();
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = Audience,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "kc-1"),
                new Claim("preferred_username", "grace"),
                new Claim("name", "Grace Hopper"),
            ]),
            Expires = TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(5),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });

        var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";

        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme).ConfigureAwait(false);
        result.Succeeded.Should().BeTrue(result.Failure?.Message ?? "the token was not accepted");
        context.User = result.Principal!;

        // Read here, not by the caller: IHttpContextAccessor keeps the context in an AsyncLocal.
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        return (user.Id, user.DisplayName, context.User.Identity?.Name);
    }

    private sealed class FakeBuilder : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
        public IHostEnvironment Environment { get; } = new FakeEnvironment();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
