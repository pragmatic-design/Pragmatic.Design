using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     HEAD/OPTIONS verb support: minimal APIs have no MapHead/MapOptions, so the generator
///     emits MapMethods with an explicit verb array. HEAD responses must not carry a body
///     (PRAG0514 + body suppression).
/// </summary>
public class HeadOptionsEndpointTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void HeadVerb_VoidEndpoint_UsesMapMethods()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace Test.Api;

            [Endpoint(HttpVerb.Head, "/api/health/ping")]
            public partial class PingHealthEndpoint : VoidEndpoint
            {
                public override Task<VoidResult> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(VoidResult.Success());
            }
            """;

        var result = RunGenerator(source);


        var generated = GetGeneratedSource(result, "PingHealthEndpoint");
        generated.Should().Contain("MapMethods(\"/api/health/ping\", new[] { \"HEAD\" },");
        generated.Should().NotContain("MapHead");
        HasDiagnostic(result, "PRAG0514").Should().BeFalse("void HEAD endpoints are the correct shape");
    }

    [Fact]
    public void OptionsVerb_VoidEndpoint_UsesMapMethods()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace Test.Api;

            [Endpoint(HttpVerb.Options, "/api/uploads")]
            public partial class UploadOptionsEndpoint : VoidEndpoint
            {
                public override Task<VoidResult> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(VoidResult.Success());
            }
            """;

        var result = RunGenerator(source);


        var generated = GetGeneratedSource(result, "UploadOptionsEndpoint");
        generated.Should().Contain("MapMethods(\"/api/uploads\", new[] { \"OPTIONS\" },");
    }

    [Fact]
    public void HeadVerb_WithResponseType_ReportsPrag0514AndSuppressesBody()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace Test.Api;

            public class PingDto { public string Status { get; set; } = "ok"; }

            [Endpoint(HttpVerb.Head, "/api/health/full")]
            public partial class FullHealthEndpoint : Endpoint<PingDto>
            {
                public override Task<Result<PingDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<PingDto>.Success(new PingDto()));
            }
            """;

        var result = RunGenerator(source);


        HasDiagnostic(result, "PRAG0514").Should().BeTrue();

        var generated = GetGeneratedSource(result, "FullHealthEndpoint");
        generated.Should().Contain("Results.StatusCode(200)", "the HEAD response body must be suppressed");
        generated.Should().NotContain("Results.Ok(success)");
    }

    [Fact]
    public void GetVerb_Endpoint_StillUsesMapGet()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace Test.Api;

            public class ItemDto { public string Name { get; set; } = ""; }

            [Endpoint(HttpVerb.Get, "/api/items")]
            public partial class GetItemsEndpoint : Endpoint<ItemDto>
            {
                public override Task<Result<ItemDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<ItemDto>.Success(new ItemDto()));
            }
            """;

        var result = RunGenerator(source);


        GetGeneratedSource(result, "GetItemsEndpoint").Should().Contain("MapGet(\"/api/items\",");
    }
}
