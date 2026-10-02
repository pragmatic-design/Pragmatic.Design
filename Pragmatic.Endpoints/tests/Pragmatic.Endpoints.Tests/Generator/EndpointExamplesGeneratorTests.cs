using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     [RequestExample]/[ResponseExample] flow into the manifest; invalid JSON raises PRAG0518.
/// </summary>
public class EndpointExamplesGeneratorTests : EndpointsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        """;

    [Fact]
    public void RequestExample_Single_FlowsIntoManifest()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class GuestDto { public string Name { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/guests")]
            [RequestExample("{ \"name\": \"Ada\" }")]
            public partial class CreateGuestEndpoint : Endpoint<GuestDto>
            {
                public required string Name { get; set; }

                public override Task<Result<GuestDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<GuestDto>.Success(new GuestDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0518").Should().BeFalse();
        var manifest = GetGeneratedSource(result, "_Metadata.PragmaticManifest");
        manifest.Should().Contain("requestExamples");
        manifest.Should().Contain("Ada");
    }

    [Fact]
    public void Examples_MultipleNamedAndResponse_FlowIntoManifest()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class GuestDto { public string Name { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/guests")]
            [RequestExample("{ \"name\": \"Ada\" }", Name = "minimal", Summary = "Minimal payload")]
            [RequestExample("{ \"name\": \"Grace\", \"vip\": true }", Name = "vip")]
            [ResponseExample(201, "{ \"id\": \"00000000-0000-0000-0000-000000000001\" }")]
            [ResponseExample(409, "{ \"code\": \"guest.duplicate\" }", Name = "duplicate")]
            public partial class CreateGuestEndpoint : Endpoint<GuestDto>
            {
                public required string Name { get; set; }

                public override Task<Result<GuestDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<GuestDto>.Success(new GuestDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0518").Should().BeFalse();
        var manifest = GetGeneratedSource(result, "_Metadata.PragmaticManifest");
        manifest.Should().Contain("\"name\": \"minimal\"");
        manifest.Should().Contain("\"summary\": \"Minimal payload\"");
        manifest.Should().Contain("\"name\": \"vip\"");
        manifest.Should().Contain("responseExamples");
        manifest.Should().Contain("\"statusCode\": 201");
        manifest.Should().Contain("\"statusCode\": 409");
        manifest.Should().Contain("guest.duplicate");
    }

    [Fact]
    public void RequestExample_InvalidJson_ReportsPrag0518()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class GuestDto { public string Name { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/guests")]
            [RequestExample("{ not valid json !!")]
            public partial class CreateGuestEndpoint : Endpoint<GuestDto>
            {
                public required string Name { get; set; }

                public override Task<Result<GuestDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<GuestDto>.Success(new GuestDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0518").Should().BeTrue();
    }
}
