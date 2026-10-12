using System.Net;
using System.Reflection;
using System.Text;
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
///     A request body whose type the configured JSON resolver does not cover fails as a server error naming the
///     type, not as 415 Unsupported Media Type; a wrong content type is still 415 (#141).
/// </summary>
/// <remarks>
///     <para>
///         The generated delegate read the body inside a <c>try</c> whose <c>catch (InvalidOperationException)</c>
///         answers 415, there for the content-type check of <c>ReadFromJsonAsync</c>. The lookup of the body's
///         <c>JsonTypeInfo</c> sat inside the same <c>try</c> and throws the same exception type when the generated
///         <c>JsonSerializerContext</c> does not cover the type, so a programmer error reached the client as
///         "unsupported media type" on a well-formed JSON request.
///     </para>
///     <para>
///         The endpoint here is the generated one, compiled and mapped into a test server: both cases end before
///         the action's invoker is resolved, so nothing else needs registering.
///     </para>
/// </remarks>
[Collection(Unit.TheProcessWideOpenApiRegistryCollection.Name)]   // loading the endpoint registers its manifest
public class ABodyWithoutJsonMetadataIsAServerErrorTests : EndpointsGeneratorTestBase
{
    // ⚠️ The global using stands in for the host's implicit usings: the generated endpoint registration calls
    // LastOrDefault by its short name (#202). Remove it with that fix.
    private const string Source = """
        global using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Notes;

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/notes")]
        public partial class AddNoteAction : DomainAction<int>
        {
            public required string Text { get; init; }

            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<int, IError>.Success(0));
        }
        """;

    private static readonly Lazy<MethodInfo> MapEndpoint = new(Compile);

    [Fact]
    public async Task ABodyTypeTheResolverDoesNotCover_IsAServerErrorNamingIt()
    {
        using var host = await Host(JsonTypeInfoResolver.Combine());   // a resolver that covers nothing

        var thrown = await Record(() => host.GetTestClient().PostAsync("/api/notes", Json("""{ "text": "hi" }""")));

        thrown.Should().BeOfType<InvalidOperationException>("a missing metadata entry is a programmer error, left to fail");
        thrown!.Message.Should().Contain("No JSON metadata");
    }

    [Fact]
    public async Task AWrongContentType_IsStill415()
    {
        using var host = await Host(new DefaultJsonTypeInfoResolver());

        var response = await host.GetTestClient().PostAsync("/api/notes",
            new StringContent("""{ "text": "hi" }""", Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<Exception?> Record(Func<Task> act)
    {
        try
        {
            await act().ConfigureAwait(false);
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private static async Task<WebApplication> Host(IJsonTypeInfoResolver resolver)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolver = resolver);

        var app = builder.Build();
        MapEndpoint.Value.Invoke(null, [app]);
        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    private static MethodInfo Compile()
    {
        // What the generated endpoint calls into at run time, which the generator tests' references leave out:
        // they read the generated text and do not compile it.
        var result = RunGenerator(Source,
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Endpoints.Binding.RequestJson)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Buffers.ReadOnlySequence<>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Uri)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Enumerable)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Threading.RateLimiting.RateLimitPartition)));
        GetCompilationErrors(result).Should().BeEmpty("the generated endpoint must compile");

        using var image = new MemoryStream();
        var emitted = result.OutputCompilation.Emit(image);
        emitted.Success.Should().BeTrue(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var assembly = Assembly.Load(image.ToArray());
        return assembly.GetTypes()
            .Select(t => t.GetMethod("MapEndpoint", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                [typeof(IEndpointRouteBuilder)]))
            .Single(m => m is not null)!;
    }
}
