using System.Net;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A typed pre- or post-processor compiles on a generated endpoint, and its typed <c>ProcessAsync</c> receives the
///     endpoint the request bound.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The generated handler called <c>ProcessAsync(endpointContext, ct)</c> on the processor's class.
///         <c>IEndpointPreProcessor&lt;T&gt;</c> implements that overload only as the interface's default, so on the class
///         the call bound to the typed overload and the endpoint failed with CS1503. The same holds for
///         <c>IEndpointPostProcessor&lt;T&gt;</c>.
///     </para>
///     <para>
///         The endpoint is the generated one, compiled and mapped into a test server, so what the processors record is
///         what they receive on a real request.
///     </para>
/// </remarks>
[Collection(Unit.TheProcessWideOpenApiRegistryCollection.Name)]   // loading the endpoint registers its manifest
public class ATypedProcessorReceivesTheEndpointTests : EndpointsGeneratorTestBase
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

        namespace TestApp.Notes;

        public static class Seen
        {
            public static object? ByThePreProcessor;
            public static object? ByThePostProcessor;
        }

        public class MarkPreProcessor : IEndpointPreProcessor<ReadNoteEndpoint>
        {
            public ValueTask<PreProcessorResult> ProcessAsync(ReadNoteEndpoint endpoint, IEndpointContext context,
                CancellationToken ct = default)
            {
                Seen.ByThePreProcessor = endpoint;
                endpoint.Mark = "pre";
                return new(PreProcessorResult.Continue());
            }
        }

        public class MarkPostProcessor : IEndpointPostProcessor<ReadNoteEndpoint>
        {
            public ValueTask ProcessAsync(ReadNoteEndpoint endpoint, IEndpointContext context, object? result,
                CancellationToken ct = default)
            {
                Seen.ByThePostProcessor = endpoint;
                return ValueTask.CompletedTask;
            }
        }

        [Endpoint(HttpVerb.Get, "/api/note")]
        [PreProcessor<MarkPreProcessor>]
        [PostProcessor<MarkPostProcessor>]
        public partial class ReadNoteEndpoint : Endpoint<string>
        {
            public string Mark { get; set; } = "none";

            public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<string>>(Mark);
        }
        """;

    private static readonly Lazy<Assembly> Compiled = new(Compile);

    [Fact]
    public async Task TheTypedPreProcessor_ReceivesTheBoundEndpoint()
    {
        using var host = await Host();

        var response = await host.GetTestClient().GetAsync("/api/note");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("\"pre\"",
            "the pre-processor wrote on the instance that HandleAsync then read");
        Recorded("ByThePreProcessor").Should().NotBeNull();
        Recorded("ByThePreProcessor")!.GetType().Name.Should().Be("ReadNoteEndpoint");
    }

    [Fact]
    public async Task TheTypedPostProcessor_ReceivesTheSameEndpoint()
    {
        using var host = await Host();

        await host.GetTestClient().GetAsync("/api/note");

        Recorded("ByThePostProcessor").Should().NotBeNull();
        Recorded("ByThePostProcessor").Should().BeSameAs(Recorded("ByThePreProcessor"),
            "both processors run on the endpoint of one request");
    }

    private static object? Recorded(string field) =>
        Compiled.Value.GetType("TestApp.Notes.Seen")!.GetField(field, BindingFlags.Static | BindingFlags.Public)!.GetValue(null);

    private static async Task<WebApplication> Host()
    {
        var assembly = Compiled.Value;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddScoped(assembly.GetType("TestApp.Notes.MarkPreProcessor")!);
        builder.Services.AddScoped(assembly.GetType("TestApp.Notes.MarkPostProcessor")!);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver());

        var app = builder.Build();
        assembly.GetTypes()
            .Select(t => t.GetMethod("MapEndpoint", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                [typeof(IEndpointRouteBuilder)]))
            .Single(m => m is not null)!
            .Invoke(null, [app]);
        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    private static Assembly Compile()
    {
        var result = RunGenerator(Source,
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Endpoints.Binding.RequestJson)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Buffers.ReadOnlySequence<>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Uri)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Enumerable)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Threading.RateLimiting.RateLimitPartition)));
        GetCompilationErrors(result).Should().BeEmpty("an endpoint with typed processors must compile");

        using var image = new MemoryStream();
        var emitted = result.OutputCompilation.Emit(image);
        emitted.Success.Should().BeTrue(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        return Assembly.Load(image.ToArray());
    }
}
