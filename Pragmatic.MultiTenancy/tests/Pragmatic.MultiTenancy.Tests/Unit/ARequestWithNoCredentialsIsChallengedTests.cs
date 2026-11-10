using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.MultiTenancy.Resolvers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     A request with no credentials, on a route that requires them, is answered 401 and not the 400 of a missing tenant (#629).
/// </summary>
/// <remarks>
///     <para>
///         When the tenant comes from the token's claim, no token is no tenant. With <c>RequireTenant</c> the resolution middleware
///         answered an empty 400 before authorization could challenge: an anonymous call and a forged token both read "bad request",
///         and a client could not tell it to sign in.
///     </para>
///     <para>
///         Measured through the pipeline the host builds, in its order: authentication (91), tenant resolution (92),
///         authorization (94), then the endpoint.
///     </para>
/// </remarks>
public sealed class ARequestWithNoCredentialsIsChallengedTests
{
    private const string TestScheme = "Test";

    /// <summary>Authenticates a request that carries <c>X-Test-User</c>, with the tenant claim it carries in <c>X-Test-Tenant</c>.</summary>
    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };
            if (Request.Headers.TryGetValue("X-Test-Tenant", out var tenant))
                claims.Add(new Claim("tenant_id", tenant.ToString()));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, TestScheme)));
        }
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }

    private static async Task<(int Status, bool Served, string Body)> Send(
        bool protectedRoute, string? user = null, string? tenant = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddHttpContextAccessor();
        services.AddOptions<MultiTenancyOptions>().Configure(o => o.RequireTenant = true);
        services.AddScoped<MutableTenantContext>();
        services.AddScoped<ITenantResolver, ClaimTenantResolver>();
        // UseMiddleware resolves every InvokeAsync parameter, the optional store included; the host registers one too.
        services.AddSingleton<ITenantStore, InMemoryTenantStore>();
        services.AddAuthentication(TestScheme).AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(TestScheme, _ => { });
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();

        var served = false;
        var app = new ApplicationBuilder(provider);
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();
        app.Run(_ => { served = true; return Task.CompletedTask; });
        var pipeline = app.Build();

        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        context.Features.Set<IEndpointFeature>(new EndpointFeature
        {
            Endpoint = new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(protectedRoute ? new AuthorizeAttribute() : new AllowAnonymousAttribute()),
                "route"),
        });
        if (user is not null)
            context.Request.Headers["X-Test-User"] = user;
        if (tenant is not null)
            context.Request.Headers["X-Test-Tenant"] = tenant;

        await pipeline(context).ConfigureAwait(false);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        return (context.Response.StatusCode, served, body);
    }

    [Fact]
    public async Task AnAnonymousRequest_ToAProtectedRoute_IsChallenged401()
    {
        var (status, served, _) = await Send(protectedRoute: true);

        status.Should().Be(StatusCodes.Status401Unauthorized, "a caller with no token is told to sign in, not that the request is malformed");
        served.Should().BeFalse("authorization refuses it, so no route runs without a tenant");
    }

    [Fact]
    public async Task AnAuthenticatedRequest_WithNoTenant_Is400WithAProblemBody()
    {
        var (status, served, body) = await Send(protectedRoute: true, user: "ada");

        status.Should().Be(StatusCodes.Status400BadRequest);
        served.Should().BeFalse();
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        problem.RootElement.GetProperty("code").GetString().Should().Be("TENANT_REQUIRED", "the code is what a client branches on");
    }

    /// <summary>An anonymous route is not let through without a tenant: nothing would refuse it afterwards.</summary>
    [Fact]
    public async Task AnAnonymousRequest_ToAnAnonymousRoute_IsStill400()
    {
        var (status, served, _) = await Send(protectedRoute: false);

        status.Should().Be(StatusCodes.Status400BadRequest);
        served.Should().BeFalse("letting it through would run a route with no tenant");
    }

    /// <summary>The control: a signed-in caller with a tenant is served.</summary>
    [Fact]
    public async Task AnAuthenticatedRequest_WithATenant_IsServed()
    {
        var (status, served, _) = await Send(protectedRoute: true, user: "ada", tenant: "acme");

        served.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }
}
