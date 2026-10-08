using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     StreamingEndpoint / StreamingDomainAction generate SSE handlers: first-item peek via
///     ToSseResult, __AdaptSse mapping Results to stream events, text/event-stream metadata,
///     and the PRAG0520-0524 constraints.
/// </summary>
public class StreamingEndpointGeneratorTests : EndpointsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        """;

    private const string SimpleStreamingEndpoint = """

        namespace Test.Api;

        public class TickDto { public int Value { get; set; } }

        [Endpoint(HttpVerb.Get, "/api/ticks")]
        public partial class TicksEndpoint : StreamingEndpoint<TickDto>
        {
            public override async IAsyncEnumerable<Result<TickDto>> HandleAsync(
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                yield return Result<TickDto>.Success(new TickDto { Value = 1 });
                await Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public void StreamingEndpoint_GeneratesSseHandler()
    {
        var result = RunGenerator(CommonUsings + SimpleStreamingEndpoint);

        var generated = GetGeneratedSource(result, "TicksEndpoint.Endpoint");
        generated.Should().Contain("SseResultExtensions.ToSseResult(");
        generated.Should().Contain("__AdaptSse(");
        generated.Should().Contain("SseStreamEvent<global::Test.Api.TickDto>.FromItem(");
        generated.Should().Contain(".WithMetadata(new global::Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(200, typeof(global::Test.Api.TickDto), new[] { \"text/event-stream\" }));");
        generated.Should().NotContain("Results.Ok(");
        generated.Should().NotContain("GeneratedJsonResponse<");
    }

    [Fact]
    public void StreamingEndpoint_WithTypedErrors_AdapterMapsEachError()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class TickDto { public int Value { get; set; } }
            public record FeedGoneError : IError
            {
                public string Code => "feed.gone";
                public int StatusCode => 409;
            }

            [Endpoint(HttpVerb.Get, "/api/ticks")]
            public partial class TicksEndpoint : StreamingEndpoint<TickDto, FeedGoneError>
            {
                public override async IAsyncEnumerable<Result<TickDto, FeedGoneError>> HandleAsync(
                    [EnumeratorCancellation] CancellationToken ct = default)
                {
                    yield return new TickDto { Value = 1 };
                    await Task.CompletedTask;
                }
            }
            """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "TicksEndpoint.Endpoint");
        generated.Should().Contain("(global::Test.Api.FeedGoneError __error)");
        generated.Should().Contain("FromError(__error)");
    }

    [Fact]
    public void StreamingEndpoint_WithSseHeartbeat_EmitsOptions()
    {
        var source = CommonUsings + SimpleStreamingEndpoint.Replace(
            "[Endpoint(HttpVerb.Get, \"/api/ticks\")]",
            "[Endpoint(HttpVerb.Get, \"/api/ticks\")]\n[Sse(HeartbeatSeconds = 15)]");

        var result = RunGenerator(source);

        GetGeneratedSource(result, "TicksEndpoint.Endpoint")
            .Should().Contain("SseStreamOptions { HeartbeatSeconds = 15 }");
    }

    [Fact]
    public void StreamingDomainAction_GeneratesSseHandlerOverInvoker()
    {
        var source = CommonUsings + """
            using Pragmatic.Actions.Abstractions;

            namespace Test.Api;

            public class SlotDto { public int Hour { get; set; } }

            [Endpoint(HttpVerb.Get, "/api/slots")]
            public partial class ScanSlotsAction : StreamingDomainAction<SlotDto>
            {
                public override async IAsyncEnumerable<Result<SlotDto, IError>> ExecuteStream(
                    [EnumeratorCancellation] CancellationToken ct = default)
                {
                    yield return Result<SlotDto, IError>.Success(new SlotDto { Hour = 9 });
                    await Task.CompletedTask;
                }
            }
            """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "ScanSlotsAction.Endpoint");
        generated.Should().Contain("invoker.InvokeAsync(action, ct)");
        generated.Should().Contain("SseResultExtensions.ToSseResult(");
        generated.Should().Contain("SseStreamEvent<global::Test.Api.SlotDto>");
    }

    [Fact]
    public void StreamingEndpoint_Constraints_ReportDiagnostics()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class TickDto { public int Value { get; set; } }

            [Endpoint(HttpVerb.Put, "/api/ticks")]
            [ResponseCache(Duration = 60)]
            [HttpStatus(202)]
            public partial class TicksEndpoint : StreamingEndpoint<TickDto>
            {
                public override async IAsyncEnumerable<Result<TickDto>> HandleAsync(
                    [EnumeratorCancellation] CancellationToken ct = default)
                {
                    yield return Result<TickDto>.Success(new TickDto());
                    await Task.CompletedTask;
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0520").Should().BeTrue("ResponseCache on streaming");
        HasDiagnostic(result, "PRAG0521").Should().BeTrue("PUT is not a streaming verb");
        HasDiagnostic(result, "PRAG0522").Should().BeTrue("HttpStatus on streaming");
    }

    /// <summary>
    ///     PRAG0523: a versioned handler on a streaming endpoint. Only the default version is
    ///     generated, and the author is told rather than left to find the missing route.
    /// </summary>
    [Fact]
    public void StreamingEndpoint_WithVersionedHandler_ReportsPrag0523()
    {
        var source = CommonUsings + SimpleStreamingEndpoint.Replace(
            "public override async IAsyncEnumerable<Result<TickDto>> HandleAsync(",
            """
            public async IAsyncEnumerable<Result<TickDto>> HandleAsyncV2(
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                yield return Result<TickDto>.Success(new TickDto { Value = 2 });
                await Task.CompletedTask;
            }

            public override async IAsyncEnumerable<Result<TickDto>> HandleAsync(
            """);

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0523").Should().BeTrue("HandleAsyncV2 declares a second version");
    }

    /// <summary>The control: the same endpoint with one handler says nothing.</summary>
    [Fact]
    public void StreamingEndpoint_WithOneHandler_DoesNotReportPrag0523()
    {
        var result = RunGenerator(CommonUsings + SimpleStreamingEndpoint);

        HasDiagnostic(result, "PRAG0523").Should().BeFalse();
    }

    /// <summary>
    ///     PRAG0524: a post-processor on a streaming endpoint, which has no final result for it to see.
    /// </summary>
    [Fact]
    public void StreamingEndpoint_WithPostProcessor_ReportsPrag0524()
    {
        var source = CommonUsings + ProcessorUsings + SimpleStreamingEndpoint.Replace(
            "[Endpoint(HttpVerb.Get, \"/api/ticks\")]",
            "[Endpoint(HttpVerb.Get, \"/api/ticks\")]\n[PostProcessor<AfterProcessor>]") + ProcessorTypes;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0524").Should().BeTrue("there is no result for a post-processor to observe");
    }

    /// <summary>
    ///     The control: a pre-processor runs before the stream opens and is allowed, so the
    ///     diagnostic is about post-processors and not about processors.
    /// </summary>
    [Fact]
    public void StreamingEndpoint_WithPreProcessorOnly_DoesNotReportPrag0524()
    {
        var source = CommonUsings + ProcessorUsings + SimpleStreamingEndpoint.Replace(
            "[Endpoint(HttpVerb.Get, \"/api/ticks\")]",
            "[Endpoint(HttpVerb.Get, \"/api/ticks\")]\n[PreProcessor<BeforeProcessor>]") + ProcessorTypes;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0524").Should().BeFalse();
    }

    private const string ProcessorUsings = """

        using Pragmatic.Endpoints.Context;
        using Pragmatic.Endpoints.Processors;
        """;

    private const string ProcessorTypes = """

        public class BeforeProcessor : IEndpointPreProcessor
        {
            public ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct = default)
                => new(PreProcessorResult.Continue());
        }

        public class AfterProcessor : IEndpointPostProcessor
        {
            public ValueTask ProcessAsync(IEndpointContext context, object? result, CancellationToken ct = default)
                => ValueTask.CompletedTask;
        }
        """;
}
