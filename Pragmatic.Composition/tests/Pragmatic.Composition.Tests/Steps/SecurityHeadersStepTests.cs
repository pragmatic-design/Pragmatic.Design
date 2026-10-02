using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Composition.Configuration;
using Pragmatic.Composition.Steps;
using Xunit;

namespace Pragmatic.Composition.Tests.Steps;

/// <summary>
///     The response headers a browser needs before it can enforce anything on the client side.
/// </summary>
public sealed class SecurityHeadersStepTests
{
    private static async Task<HttpContext> RunAsync(
        SecurityHeadersOptions? options = null, bool https = false, Action<HttpContext>? handler = null)
    {
        var services = new ServiceCollection();
        if (options is not null)
            services.AddSingleton<IOptions<SecurityHeadersOptions>>(Options.Create(options));

        var app = new ApplicationBuilder(services.BuildServiceProvider());
        new SecurityHeadersStep().ConfigurePipeline(app);

        app.Run(async ctx =>
        {
            handler?.Invoke(ctx);
            // Starting the response is what commits the headers — the step writes them on that event,
            // because after it nothing can change them.
            await ((CallbackInvokingResponseFeature)ctx.Features.Get<IHttpResponseFeature>()!)
                .FireStartingAsync().ConfigureAwait(false);
        });

        var context = new DefaultHttpContext();
        context.Request.Scheme = https ? "https" : "http";

        // DefaultHttpContext's response feature accepts OnStarting callbacks and never invokes them,
        // so a step that writes headers there would look like it does nothing. Substituting a feature
        // that actually runs them is what makes this test measure the production path rather than the
        // harness — the first version of this test passed its negative assertions for exactly the wrong
        // reason: no header was ever written at all.
        var response = new CallbackInvokingResponseFeature();
        context.Features.Set<IHttpResponseFeature>(response);

        await app.Build().Invoke(context).ConfigureAwait(false);
        return context;
    }

    /// <summary>A response feature that runs its <c>OnStarting</c> callbacks when the response starts.</summary>
    private sealed class CallbackInvokingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state) { }

        /// <summary>Invoked by <c>Response.StartAsync()</c>, which is what the pipeline calls.</summary>
        internal async Task FireStartingAsync()
        {
            if (HasStarted)
                return;

            HasStarted = true;

            // Reverse order, as the real server does: the callback registered first runs last, so an
            // outer middleware gets the final say.
            for (var i = _onStarting.Count - 1; i >= 0; i--)
                await _onStarting[i].Callback(_onStarting[i].State).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task ByDefault_TheBaselineHeadersArePresent()
    {
        var context = await RunAsync();

        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
        context.Response.Headers["Content-Security-Policy"].ToString().Should().Contain("default-src 'none'");
    }

    [Fact]
    public async Task OverHttp_HstsIsNotSent()
    {
        // A browser ignores HSTS on a plain connection, and honouring it there would mean pinning a
        // policy learned from a channel that could have been tampered with.
        var context = await RunAsync(https: false);

        context.Response.Headers.Should().NotContainKey("Strict-Transport-Security");
    }

    [Fact]
    public async Task OverHttps_HstsIsSentWithSubdomains()
    {
        var context = await RunAsync(https: true);

        context.Response.Headers["Strict-Transport-Security"].ToString()
            .Should().Be("max-age=31536000; includeSubDomains");
    }

    [Fact]
    public async Task AHeaderAlreadySetByTheHandler_IsNotOverwritten()
    {
        // An endpoint serving HTML needs a different policy from the API default. The framework must
        // not overrule the person who knew the case.
        var context = await RunAsync(handler: ctx =>
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'");

        context.Response.Headers["Content-Security-Policy"].ToString().Should().Be("default-src 'self'");
    }

    [Fact]
    public async Task Disabled_EmitsNothing()
    {
        var context = await RunAsync(new SecurityHeadersOptions { Enabled = false });

        context.Response.Headers.Should().NotContainKey("X-Content-Type-Options");
    }

    [Fact]
    public async Task ANullValue_OmitsThatHeaderRatherThanEmittingAnEmptyOne()
    {
        var context = await RunAsync(new SecurityHeadersOptions { FrameOptions = null, ReferrerPolicy = null });

        context.Response.Headers.Should().NotContainKey("X-Frame-Options");
        context.Response.Headers.Should().NotContainKey("Referrer-Policy");
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task AdditionalHeaders_AreEmitted()
    {
        var options = new SecurityHeadersOptions();
        options.Additional["Permissions-Policy"] = "geolocation=()";

        var context = await RunAsync(options);

        context.Response.Headers["Permissions-Policy"].ToString().Should().Be("geolocation=()");
    }

    [Fact]
    public async Task HstsMaxAgeZero_OmitsTheHeader()
    {
        var context = await RunAsync(
            new SecurityHeadersOptions { StrictTransportSecurityMaxAgeSeconds = 0 }, https: true);

        context.Response.Headers.Should().NotContainKey("Strict-Transport-Security");
    }
}
