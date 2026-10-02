using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Composition.Steps;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     The step ships in the package. Without it an endpoint carrying authorization metadata answers
///     500 — "contains authorization metadata, but a middleware was not found that supports
///     authorization" — unless the consumer writes the step by hand.
/// </summary>
public class AuthenticationStepTests
{
    /// <summary>
    ///     After routing (50) and before InternationalizationStep (93), which resolves the culture from
    ///     the authenticated user and so has to run after.
    /// </summary>
    [Fact]
    public void Order_SitsAfterRoutingAndBeforeCulture()
    {
        new AuthenticationStep().Order.Should().Be(91);
    }

    [Fact]
    public async Task WithoutAuthenticationRegistered_ThePipelineStillRuns()
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        new AuthenticationStep().ConfigurePipeline(app);
        app.Run(_ => Task.CompletedTask);

        // Unguarded, UseAuthentication() puts AuthenticationMiddleware in the pipeline and it throws
        // on construction for the missing scheme provider — turning "this app has no login" into a
        // 500 on every request.
        var act = async () => await app.Build()(new DefaultHttpContext()).ConfigureAwait(false);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task WithAuthenticationRegistered_TheMiddlewareActuallyRuns()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();   // UseAuthorization resolves EndpointDataSource
        services.AddAuthentication("Stub").AddScheme<AuthenticationSchemeOptions, StubHandler>("Stub", null);
        services.AddAuthorization();

        var app = new ApplicationBuilder(services.BuildServiceProvider());
        new AuthenticationStep().ConfigurePipeline(app);

        ClaimsPrincipal? seen = null;
        app.Run(ctx =>
        {
            seen = ctx.User;
            return Task.CompletedTask;
        });

        await app.Build()(new DefaultHttpContext { RequestServices = services.BuildServiceProvider() })
            .ConfigureAwait(true);

        // Asserting "did not throw" would pass with no middleware at all. The claim only exists if
        // AuthenticationMiddleware ran and assigned the result to HttpContext.User.
        seen!.FindFirst("stub")!.Value.Should().Be("ran");
    }

    private sealed class StubHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, NullLoggerFactory.Instance, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim("stub", "ran")], "Stub");
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Stub")));
        }
    }
}
