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

namespace Pragmatic.Identity.Oidc.Tests;

/// <summary>
///     A token from the OpenID Connect provider, validated by the handler this package registers,
///     reaches the current user with the name <see cref="OidcOptions.NameClaim" /> points at.
/// </summary>
/// <remarks>
///     <para>
///         <c>NameClaim</c> set the validator's <c>NameClaimType</c> and nothing else, so
///         <c>User.Identity.Name</c> followed it and <c>ICurrentUser.DisplayName</c> read the WS-* name
///         URI, which JwtBearer never produces from <c>name</c>. The option half-worked: it named the
///         caller for ASP.NET Core and for nobody in Pragmatic.
///     </para>
///     <para>
///         The provider's discovery document is replaced by a static configuration carrying the
///         signing key, so the handler validates for real without an identity provider to ask.
///     </para>
/// </remarks>
public class TheTokenReachesTheCurrentUserTests
{
    private const string Authority = "https://idp.example.com";
    private const string Audience = "my-api";

    private static readonly SymmetricSecurityKey Key =
        new(Encoding.UTF8.GetBytes("oidc-round-trip-signing-key-32-chars"));

    [Fact]
    public async Task TheDefaultNameClaim_IsTheDisplayName()
    {
        var (_, displayName) = await AuthenticateAsync(nameClaim: null);

        displayName.Should().Be("Grace Hopper");
    }

    [Fact]
    public async Task ANameClaimTheApplicationChose_IsTheDisplayName()
    {
        var (_, displayName) = await AuthenticateAsync(nameClaim: "nickname");

        displayName.Should().Be("Amazing Grace");
    }

    /// <summary>The control: the token is accepted and the subject arrives, today and after.</summary>
    [Fact]
    public async Task TheSubject_IsTheId()
    {
        var (id, _) = await AuthenticateAsync(nameClaim: null);

        id.Should().Be("idp-1");
    }

    private static async Task<(string Id, string? DisplayName)> AuthenticateAsync(string? nameClaim)
    {
        var builder = new FakeBuilder();
        builder.Services.AddLogging();

        // What the generated host registers, before the application's callback runs.
        builder.Services.AddPragmaticIdentity();
        builder.UseOidcAuthentication(o =>
        {
            o.Authority = Authority;
            o.Audience = Audience;
            if (nameClaim is not null)
                o.NameClaim = nameClaim;
        });

        // The discovery document, without a provider: the handler takes a static configuration when
        // one is set, and never goes to the network.
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
                new Claim("sub", "idp-1"),
                new Claim("name", "Grace Hopper"),
                new Claim("nickname", "Amazing Grace"),
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

        return (user.Id, user.DisplayName);
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
