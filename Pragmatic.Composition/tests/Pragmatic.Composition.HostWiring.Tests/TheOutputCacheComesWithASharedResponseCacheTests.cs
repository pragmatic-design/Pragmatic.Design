// Pragmatic.Composition.HostWiring.Tests - A shared [ResponseCache] is a declaration

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A module that declares a response any caller may share — <c>[ResponseCache]</c> with its default
///     location — gets the output cache in its host: the services and the middleware.
/// </summary>
/// <remarks>
///     The generator turned the attribute into <c>CacheOutput(…)</c> on the route and the host never added
///     <c>AddOutputCache</c> or <c>UseOutputCache</c>, so the attribute stored nothing anywhere and nothing
///     said so (measured in <c>WhatASharedResponseCacheKeepsTests</c>, whose host adds them by hand).
/// </remarks>
public sealed class TheOutputCacheComesWithASharedResponseCacheTests
{
    private const string HostServices = "Host.Services.g.cs";
    private const string Services = "services.AddOutputCache();";
    private const string Step =
        "services.AddSingleton<global::Pragmatic.Composition.Abstractions.IStartupStep, global::Pragmatic.Composition.Steps.OutputCacheStep>();";

    [Fact]
    public void ASharedResponseCache_GetsTheOutputCache()
    {
        var lines = HostWiringFixture.LinesOf(
            HostWiringFixture.GeneratedHostOf("Probe.SharedCache", Endpoint("[ResponseCache(Duration = 60)]")), HostServices);

        lines.Should().Contain(Services);
        lines.Should().Contain(Step);
    }

    /// <summary>A response kept by the browser alone needs no server cache: nothing is added.</summary>
    [Fact]
    public void AClientOnlyResponseCache_GetsNothing()
    {
        var lines = HostWiringFixture.LinesOf(
            HostWiringFixture.GeneratedHostOf(
                "Probe.ClientCache", Endpoint("[ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client)]")),
            HostServices);

        lines.Should().NotContain(Services);
        lines.Should().NotContain(Step);
    }

    /// <summary>The control: an endpoint that declares no cache gets none.</summary>
    [Fact]
    public void NoResponseCache_GetsNothing()
    {
        var lines = HostWiringFixture.LinesOf(
            HostWiringFixture.GeneratedHostOf("Probe.NoCache", Endpoint("")), HostServices);

        lines.Should().NotContain(Services);
        lines.Should().NotContain(Step);
    }

    private static string Endpoint(string cache) => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace Probe.Rates;

        [Endpoint(HttpVerb.Get, "api/rates")]
        [AllowAnonymous]
        {{cache}}
        public partial class GetRatesEndpoint : Endpoint<string>
        {
            public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult(Result<string>.Success("rates"));
        }
        """;
}
