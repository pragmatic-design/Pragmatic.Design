using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Steps;
using Pragmatic.Http;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Local.Jwt.Tests.Unit;

/// <summary>
///     <c>UseJwtAuthentication</c> caps the sign-in endpoints per client address — the exposed
///     <c>LoginUser</c> and <c>SignInUser</c>, on whatever route — and leaves every other endpoint alone.
/// </summary>
/// <remarks>
///     The limit was a startup step nothing registered, matching a path Time off does not use. The
///     pipeline here is the host's: the registered startup steps, in their order, after routing. Under
///     TestServer every request comes from the same (absent) address, so they share one bucket.
/// </remarks>
public class TheSignInIsRateLimitedTests
{
    private const string Key = "sign-in-rate-limit-signing-key-32-bytes!";

    [Fact]
    public async Task MoreSignInsThanTheLimit_Answer429()
    {
        var app = await StartAsync(permitLimit: 2);
        await using var running = app.ConfigureAwait(true);
        using var client = app.GetTestClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await client.PostAsync("/identity/local/sign-in", null)).StatusCode);

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
    }

    /// <summary>The operation decides, not the path: <c>LoginUser</c> on a route of the application's choosing.</summary>
    [Fact]
    public async Task TheLimit_FollowsTheOperation_NotTheRoute()
    {
        var app = await StartAsync(permitLimit: 1);
        await using var running = app.ConfigureAwait(true);
        using var client = app.GetTestClient();

        (await client.PostAsync("/accounts/login", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/accounts/login", null)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>The control: the limit is not a limit on the application.</summary>
    [Fact]
    public async Task OtherEndpoints_AreNotLimited()
    {
        var app = await StartAsync(permitLimit: 1);
        await using var running = app.ConfigureAwait(true);
        using var client = app.GetTestClient();

        for (var i = 0; i < 5; i++)
            (await client.GetAsync("/api/other")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The application's own rejection status is kept for its limits; a refused sign-in is still 429.</summary>
    [Fact]
    public async Task ASignInRejection_Is429_WhateverTheApplicationChoseForItsOwnLimits()
    {
        var app = await StartAsync(permitLimit: 1,
            before: services => services.AddRateLimiter(o => o.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable));
        await using var running = app.ConfigureAwait(true);
        using var client = app.GetTestClient();

        await client.PostAsync("/identity/local/sign-in", null);
        (await client.PostAsync("/identity/local/sign-in", null)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Disabled_NothingIsLimited()
    {
        var app = await StartAsync(permitLimit: 1, enabled: false);
        await using var running = app.ConfigureAwait(true);
        using var client = app.GetTestClient();

        for (var i = 0; i < 3; i++)
            (await client.PostAsync("/identity/local/sign-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    ///     One middleware: the host's step when it already added it, this package's when it did not. Two
    ///     would spend two permits per sign-in.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheMiddleware_IsRegisteredOnce(bool hostAddedIt)
    {
        var builder = new Builder(WebApplication.CreateBuilder(), Settings(permitLimit: 1, enabled: true));
        if (hostAddedIt)
            builder.Services.AddSingleton<IStartupStep, RateLimiterStep>();

        builder.UseJwtAuthentication(jwt => jwt.SigningKey = Key);

        builder.Services.Count(d => d.ServiceType == typeof(IStartupStep) && d.ImplementationType == typeof(RateLimiterStep))
            .Should().Be(1);
    }

    private static async Task<WebApplication> StartAsync(
        int permitLimit, bool enabled = true, Action<IServiceCollection>? before = null)
    {
        var web = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        web.WebHost.UseTestServer();
        before?.Invoke(web.Services);
        new Builder(web, Settings(permitLimit, enabled)).UseJwtAuthentication(jwt => jwt.SigningKey = Key);

        var app = web.Build();
        app.UseRouting();
        foreach (var step in app.Services.GetServices<IStartupStep>().OrderBy(s => s.Order))
            step.ConfigurePipeline(app);

        app.MapPost("/identity/local/sign-in", () => Results.Ok())
            .WithMetadata(new ExposedOperationMetadata(typeof(SignInUser))).AllowAnonymous();
        app.MapPost("/accounts/login", () => Results.Ok())
            .WithMetadata(new ExposedOperationMetadata(typeof(LoginUser))).AllowAnonymous();
        app.MapGet("/api/other", () => Results.Ok()).AllowAnonymous();

        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    private static Dictionary<string, string?> Settings(int permitLimit, bool enabled) => new()
    {
        ["Identity:Local:RateLimit:Enabled"] = enabled.ToString(),
        ["Identity:Local:RateLimit:PermitLimit"] = permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Identity:Local:RateLimit:Window"] = "00:10:00"
    };

    private sealed class Builder : IPragmaticBuilder
    {
        public Builder(WebApplicationBuilder web, Dictionary<string, string?> settings)
        {
            web.Configuration.AddInMemoryCollection(settings);
            Services = web.Services;
            Configuration = web.Configuration;
            Environment = web.Environment;
        }

        public IServiceCollection Services { get; }
        public IConfiguration Configuration { get; }
        public IHostEnvironment Environment { get; }
    }
}
