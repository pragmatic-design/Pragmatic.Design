using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     PRAG0554: a shared <c>[ResponseCache]</c> on a route that requires authentication keeps nothing,
///     because the output cache never keeps a request that carries credentials — measured in
///     <c>WhatASharedResponseCacheKeepsTests</c>. The build says so instead of the attribute lying.
/// </summary>
public class ASharedResponseCacheNeedsAnAnonymousRouteTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void ASharedCache_OnAnAuthenticatedRoute_Warns()
    {
        var result = RunGenerator(Source("[ResponseCache(Duration = 60)]"));

        HasDiagnostic(result, "PRAG0554").Should().BeTrue();
    }

    /// <summary>The control: the same cache on an anonymous route is what it is for.</summary>
    [Fact]
    public void ASharedCache_OnAnAnonymousRoute_IsQuiet()
    {
        var result = RunGenerator(Source("[ResponseCache(Duration = 60)]\n[AllowAnonymous]"));

        HasDiagnostic(result, "PRAG0554").Should().BeFalse();
    }

    /// <summary>A browser-only cache is a header, and works for an authenticated caller.</summary>
    [Fact]
    public void AClientCache_OnAnAuthenticatedRoute_IsQuiet()
    {
        var result = RunGenerator(Source("[ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client)]"));

        HasDiagnostic(result, "PRAG0554").Should().BeFalse();
    }

    private static string Source(string attributes) => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace TestApp;

        [Endpoint(HttpVerb.Get, "/api/rates")]
        {{attributes}}
        public partial class GetRatesEndpoint : Endpoint<string>
        {
            public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult(Result<string>.Success("rates"));
        }
        """;
}
