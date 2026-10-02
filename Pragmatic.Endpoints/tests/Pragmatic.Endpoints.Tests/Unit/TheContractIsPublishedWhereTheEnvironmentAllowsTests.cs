using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     The generated host publishes the compile-time document in Development, everywhere after
///     <c>UseApiDocumentation()</c>, and never on top of a route the application mapped itself.
/// </summary>
/// <remarks>
///     <para>
///         Asserted on the route table, not on a response: <c>MapPragmaticOpenApi</c> answers 404 when no
///         document was registered, so a 404 could not tell "not mapped" from "mapped, nothing to serve".
///     </para>
///     <para>
///         ⚠️ No case here registers a document: <c>PragmaticOpenApiRegistry</c> is process-wide, and
///         <see cref="ThePublishedContractHonoursEnableOpenApiTests" /> reads back the one it registers.
///         That the mapped route serves the document is measured in the Time off and Conformance suites.
///     </para>
/// </remarks>
public sealed class TheContractIsPublishedWhereTheEnvironmentAllowsTests
{
    private static readonly InteractiveApiReference Reference =
        new("/scalar", endpoints => endpoints.MapGet("/scalar/{documentName?}", () => "reference"));

    [Fact]
    public void InDevelopment_TheDocumentIsPublished()
    {
        using var app = Build(Environments.Development);

        ApiDocumentationMapper.Map(app);

        RoutesOf(app).Should().Contain(ApiDocumentationMapper.DocumentPath);
    }

    /// <summary>The control: outside Development nobody asked for it.</summary>
    [Fact]
    public void InProduction_TheDocumentIsNotPublished()
    {
        using var app = Build(Environments.Production);

        ApiDocumentationMapper.Map(app);

        RoutesOf(app).Should().NotContain(ApiDocumentationMapper.DocumentPath);
    }

    [Fact]
    public void InProduction_AfterUseApiDocumentation_TheDocumentIsPublished()
    {
        using var app = Build(Environments.Production, builder => builder.UseApiDocumentation());

        ApiDocumentationMapper.Map(app);

        RoutesOf(app).Should().Contain(ApiDocumentationMapper.DocumentPath);
    }

    [Fact]
    public void WhenTheApplicationMappedTheDocumentItself_ItIsMappedOnce()
    {
        using var app = Build(Environments.Development);
        app.MapPragmaticOpenApi();

        ApiDocumentationMapper.Map(app);

        RoutesOf(app).Count(route => route == ApiDocumentationMapper.DocumentPath).Should().Be(1,
            "two endpoints on one route fail with AmbiguousMatchException on the first request");
    }

    [Fact]
    public void InDevelopment_TheReferenceIsMapped()
    {
        using var app = Build(Environments.Development);

        ApiDocumentationMapper.Map(app, Reference);

        RoutesOf(app).Should().Contain("/scalar/{documentName?}");
    }

    /// <summary>The document is a contract a client may fetch; the reference is a tool for developers.</summary>
    [Fact]
    public void InProduction_TheReferenceIsNotMapped_EvenWhenTheDocumentIs()
    {
        using var app = Build(Environments.Production, builder => builder.UseApiDocumentation());

        ApiDocumentationMapper.Map(app, Reference);

        var routes = RoutesOf(app);
        routes.Should().Contain(ApiDocumentationMapper.DocumentPath);
        routes.Should().NotContain("/scalar/{documentName?}");
    }

    [Fact]
    public void WhenTheApplicationMappedTheReferenceItself_ItIsNotMappedAgain()
    {
        using var app = Build(Environments.Development);
        app.MapGet("/scalar/v1", () => "the application's own");
        var mapped = 0;

        ApiDocumentationMapper.Map(app, new InteractiveApiReference("/scalar", _ => mapped++));

        mapped.Should().Be(0, "a route under /scalar is already there");
    }

    /// <summary>
    ///     The reference is an HTML page that runs scripts, and the host's default policy for an API denies
    ///     every script: the reference states its own, which the security headers leave in place.
    /// </summary>
    [Fact]
    public async Task TheReference_AnswersWithAPolicyThatLetsItsPageRun()
    {
        await using var app = Build(Environments.Development);
        ApiDocumentationMapper.Map(app, Reference);
        await app.StartAsync();

        using var response = await app.GetTestClient().GetAsync(new Uri("/scalar/v1", UriKind.Relative));

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.Should().Contain("script-src 'self' 'unsafe-inline'");
        policy.Should().Contain("frame-ancestors 'none'");
    }

    private static WebApplication Build(string environment, Action<IPragmaticBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        configure?.Invoke(new Builder(builder));
        return builder.Build();
    }

    private static List<string?> RoutesOf(IEndpointRouteBuilder app)
        => [.. app.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)];

    private sealed class Builder(WebApplicationBuilder builder) : IPragmaticBuilder
    {
        public IServiceCollection Services => builder.Services;
        public IConfiguration Configuration => builder.Configuration;
        public IHostEnvironment Environment => builder.Environment;
    }
}
