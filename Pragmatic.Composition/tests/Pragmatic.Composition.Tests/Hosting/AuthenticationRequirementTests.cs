using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Composition.Hosting;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     A host whose endpoints require authorization, and that configures no authentication
///     method in the environment it runs in, refuses to start and says what is missing. Started anyway, it
///     would answer 500 to every protected request ("contains authorization metadata, but a middleware was
///     not found that supports authorization") — the case of a scaffold started without a launch profile,
///     running in Production with the development identity configured for Development only.
/// </summary>
public class AuthenticationRequirementTests
{
    [Fact]
    public async Task AProtectedEndpoint_WithNoAuthenticationMethod_RefusesToStart()
    {
        await using var app = Host("Production", configure: _ => { });
        app.MapGet("/api/items", () => "items").RequireAuthorization();

        var act = () => AuthenticationRequirement.VerifyAsync(app, app.Environment);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true))
            .WithMessage("*no authentication method is configured for environment Production*UseDevelopmentIdentity*");
    }

    /// <summary>Registered authorization with no scheme cannot challenge anyone either: the same refusal.</summary>
    [Fact]
    public async Task AuthorizationWithoutAScheme_RefusesToStart()
    {
        await using var app = Host("Production", services => services.AddAuthorization());
        app.MapGet("/api/items", () => "items").RequireAuthorization();

        var act = () => AuthenticationRequirement.VerifyAsync(app, app.Environment);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    /// <summary>The control: an authentication method configured, the same endpoint starts.</summary>
    [Fact]
    public async Task WithAnAuthenticationMethod_ItStarts()
    {
        await using var app = Host("Development", services =>
        {
            services.AddAuthentication("Stub").AddScheme<AuthenticationSchemeOptions, StubHandler>("Stub", null);
            services.AddAuthorization();
        });
        app.MapGet("/api/items", () => "items").RequireAuthorization();

        var act = () => AuthenticationRequirement.VerifyAsync(app, app.Environment);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    /// <summary>The other control: nothing requires authorization, nothing is required.</summary>
    [Fact]
    public async Task WithNoProtectedEndpoint_ItStarts()
    {
        await using var app = Host("Production", configure: _ => { });
        app.MapGet("/openapi/v1.json", () => "{}");

        var act = () => AuthenticationRequirement.VerifyAsync(app, app.Environment);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    private static WebApplication Host(string environment, Action<IServiceCollection> configure)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        configure(builder.Services);
        return builder.Build();
    }

    private sealed class StubHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("Stub")), "Stub")));
    }
}
