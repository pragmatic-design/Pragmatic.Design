using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A processor named by <c>[PreProcessor&lt;T&gt;]</c> / <c>[PostProcessor&lt;T&gt;]</c> is resolved
///     from the request container by the generated handler, so the generator that emitted that
///     resolution also emits the registration it needs.
/// </summary>
/// <remarks>
///     The handler has always called <c>GetRequiredService&lt;TProcessor&gt;()</c> and nothing in the
///     repository registered the type: every request to a route carrying a processor answered 500 with
///     "No service for type '…' has been registered". The half that was missing is here.
/// </remarks>
public class ProcessorRegistrationTests : EndpointsGeneratorTestBase
{
    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Endpoints.Context;
        using Pragmatic.Endpoints.Processors;
        using Pragmatic.Result;
        """;

    private const string WithProcessors = Usings + """

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

    private const string WithoutProcessors = Usings + """

        namespace Test.Api;

        public class NoteDto { public string Text { get; set; } = ""; }

        [Endpoint(HttpVerb.Get, "/api/notes")]
        public partial class GetNotesEndpoint : Endpoint<NoteDto>
        {
            public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
        }
        """;

    [Fact]
    public void DeclaredProcessors_AreRegisteredInTheAssemblyRegistration()
    {
        var result = RunGenerator(WithProcessors);

        var registration = GetGeneratedSource(result, "_Infra.Endpoints.Registration");
        registration.Should().NotBeNull();
        registration!.Should().Contain("TryAddScoped<global::Test.Api.BeforeProcessor>");
        registration.Should().Contain("TryAddScoped<global::Test.Api.AfterProcessor>");
    }

    /// <summary>
    ///     The control case: without a processor the registration says nothing about processors. An
    ///     assertion that only ever looks for a line present would also pass if the generator emitted
    ///     that line for every assembly.
    /// </summary>
    [Fact]
    public void NoProcessorDeclared_TheRegistrationCarriesNoProcessorLine()
    {
        var result = RunGenerator(WithoutProcessors);

        var registration = GetGeneratedSource(result, "_Infra.Endpoints.Registration");
        registration.Should().NotBeNull();
        registration!.Should().NotContain("TryAddScoped");
    }

    /// <summary>
    ///     A processor the container cannot construct is the one case the registration cannot cover.
    /// </summary>
    [Fact]
    public void AbstractProcessor_ReportsPrag0534()
    {
        var source = Usings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            public abstract class AbstractPreProcessor : IEndpointPreProcessor
            {
                public ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct = default)
                    => new(PreProcessorResult.Continue());
            }

            [Endpoint(HttpVerb.Get, "/api/notes")]
            [PreProcessor<AbstractPreProcessor>]
            public partial class GetNotesEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0534").Should().BeTrue(
            "an abstract processor cannot be constructed by the container");
        GetDiagnosticsById(result, "PRAG0534").First().GetMessage()
            .Should().Contain("AbstractPreProcessor");
    }

    [Fact]
    public void ProcessorWithOnlyAPrivateConstructor_ReportsPrag0534()
    {
        var source = Usings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            public class PrivateCtorPreProcessor : IEndpointPreProcessor
            {
                private PrivateCtorPreProcessor() { }

                public ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct = default)
                    => new(PreProcessorResult.Continue());
            }

            [Endpoint(HttpVerb.Get, "/api/notes")]
            [PreProcessor<PrivateCtorPreProcessor>]
            public partial class GetNotesEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0534").Should().BeTrue(
            "the container resolves through public constructors only");
    }

    /// <summary>
    ///     The control for PRAG0534: a processor with a public constructor taking injected services is
    ///     exactly what the feature is for, and must stay silent.
    /// </summary>
    [Fact]
    public void ProcessorWithInjectedDependencies_DoesNotReportPrag0534()
    {
        var source = Usings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            public interface INoteService { }

            public class InjectingPreProcessor(INoteService notes) : IEndpointPreProcessor
            {
                public ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct = default)
                    => new(PreProcessorResult.Continue());
            }

            [Endpoint(HttpVerb.Get, "/api/notes")]
            [PreProcessor<InjectingPreProcessor>]
            public partial class GetNotesEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0534").Should().BeFalse();
        GetGeneratedSource(result, "_Infra.Endpoints.Registration")
            .Should().Contain("TryAddScoped<global::Test.Api.InjectingPreProcessor>");
    }
}
