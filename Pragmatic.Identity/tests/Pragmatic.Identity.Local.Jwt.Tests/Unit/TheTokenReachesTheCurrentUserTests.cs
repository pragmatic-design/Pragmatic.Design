using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Pragmatic.Composition;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Local.Jwt.Tests.Unit;

/// <summary>
///     A token this package mints, validated by the handler this package registers, reaches the
///     current user whole: the id, the name and the roles.
/// </summary>
/// <remarks>
///     <para>
///         Three pieces name the display-name claim. The generator writes <c>name</c>, the validator's
///         <c>NameClaimType</c> is <c>name</c>, and the accessor read
///         <c>IdentityOptions.DisplayNameClaimType</c>, whose default is the WS-* name URI. JwtBearer
///         renames <c>sub</c> and <c>role</c> to their WS-* URIs on the way in, which is why ids and
///         Pragmatic's own permission checks worked. It does not rename <c>name</c>, so
///         <c>ICurrentUser.DisplayName</c> was null for every bearer caller.
///     </para>
///     <para>
///         Found by shunpo, where every comment and note written with a bearer token was
///         stored without an author. Each half had tests of its own; none carried a token from one to
///         the other, which is the only place the disagreement exists.
///     </para>
/// </remarks>
public class TheTokenReachesTheCurrentUserTests
{
    private const string Key = "round-trip-signing-key-at-least-32-chars!";
    private const string Issuer = "https://app.example.com";
    private const string Audience = "app-api";

    [Fact]
    public async Task TheName_IsTheDisplayName()
    {
        var (user, _) = await AuthenticateAsync();

        user.DisplayName.Should().Be("Grace Hopper");
    }

    /// <summary>
    ///     <c>IsInRole</c> and <c>[Authorize(Roles = …)]</c> read the identity's role claim type, which
    ///     the validator sets; the claims arrive under the name JwtBearer gave them.
    /// </summary>
    [Fact]
    public async Task TheRoles_AreRolesToAspNetCore()
    {
        var (_, principal) = await AuthenticateAsync();

        principal.IsInRole("owner").Should().BeTrue();
    }

    /// <summary>The control: the token is accepted and the subject arrives, today and after.</summary>
    /// <remarks>
    ///     Without it the two cases above could be red because nothing authenticated at all.
    /// </remarks>
    [Fact]
    public async Task TheSubject_IsTheId()
    {
        var (user, _) = await AuthenticateAsync();

        user.Id.Should().Be("u-1");
    }

    /// <summary>
    ///     The entry point states a default, and an application that names another claim is obeyed.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not possible before: <c>AddPragmaticIdentity()</c>, which every generated host calls,
    ///     registered <c>IOptions&lt;IdentityOptions&gt;</c> as a fixed instance, and a closed
    ///     registration wins over the options pattern — so every <c>Configure&lt;IdentityOptions&gt;</c>,
    ///     the entry point's and the application's alike, was ignored without a word.
    /// </remarks>
    [Fact]
    public async Task AnApplicationThatNamesAnotherClaim_IsObeyed()
    {
        var (user, _) = await AuthenticateAsync(
            services => services.Configure<IdentityOptions>(o => o.DisplayNameClaimType = "nickname"),
            extraClaim: new Claim("nickname", "Amazing Grace"));

        user.DisplayName.Should().Be("Amazing Grace");
    }

    private static async Task<(Seen User, ClaimsPrincipal Principal)> AuthenticateAsync(
        Action<IServiceCollection>? applicationConfiguration = null,
        Claim? extraClaim = null)
    {
        var builder = new FakeBuilder();
        builder.Services.AddLogging();

        // What the generated host registers, before the application's callback runs.
        builder.Services.AddPragmaticIdentity();
        builder.UseJwtAuthentication(jwt =>
        {
            jwt.SigningKey = Key;
            jwt.Issuer = Issuer;
            jwt.Audience = Audience;
            jwt.RequireSecurityStamp = false;
        });
        applicationConfiguration?.Invoke(builder.Services);

        var provider = builder.Services.BuildServiceProvider();
        var token = provider.GetRequiredService<JwtTokenGenerator>()
            .Generate("u-1", displayName: "Grace Hopper", roles: ["owner"]).Token;

        if (extraClaim is not null)
            token = WithClaim(extraClaim);

        var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";

        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme).ConfigureAwait(false);
        result.Succeeded.Should().BeTrue(result.Failure?.Message ?? "the token was not accepted");

        context.User = result.Principal!;

        // Read here, not by the caller: IHttpContextAccessor keeps the context in an AsyncLocal, and a
        // value set inside this async method is gone once it returns.
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        return (new Seen(user.Id, user.DisplayName), context.User);
    }

    private sealed record Seen(string Id, string? DisplayName);

    /// <summary>The generator has no parameter for an arbitrary claim, so this one is minted by hand.</summary>
    private static string WithClaim(Claim claim)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity([new Claim("sub", "u-1"), new Claim("name", "Grace Hopper"), claim]),
            Expires = TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
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
