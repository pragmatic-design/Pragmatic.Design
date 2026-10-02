using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     The processor attributes — <c>[PreProcessor&lt;T&gt;]</c>, <c>[PostProcessor&lt;T&gt;]</c> — put
///     their processors in the generated pipeline.
/// </summary>
/// <remarks>
///     Asserted on the pipeline itself, not on the absence of a diagnostic: silence is also what an
///     attribute the generator never recognises produces.
/// </remarks>
public class GenericProcessorAttributeTests : EndpointsGeneratorTestBase
{
    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Endpoints.Context;
        using Pragmatic.Endpoints.Processors;
        using Pragmatic.Result;

        namespace Test.Api;

        public class NoteDto { public string Text { get; set; } = ""; }

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

        [Endpoint(HttpVerb.Get, "/api/notes")]
        [PreProcessor<BeforeProcessor>]
        [PostProcessor<AfterProcessor>]
        public partial class GetNotesEndpoint : Endpoint<NoteDto>
        {
            public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
        }
        """;

    [Fact]
    public void GenericProcessorAttributes_GenerateTheProcessorPipeline()
    {
        var result = RunGenerator(Source);

        var handler = GetGeneratedSource(result, "GetNotesEndpoint.Endpoint");
        handler.Should().Contain("GetRequiredService<global::Test.Api.BeforeProcessor>()");
        handler.Should().Contain("GetRequiredService<global::Test.Api.AfterProcessor>()");
    }
}
