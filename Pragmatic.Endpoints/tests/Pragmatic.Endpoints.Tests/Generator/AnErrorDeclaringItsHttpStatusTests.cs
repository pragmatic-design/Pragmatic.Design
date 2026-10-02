using System.IO;
using Microsoft.CodeAnalysis;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     <c>[HttpStatus(n)]</c> on an error type sets the status the endpoint documents for it — including
///     when the attribute is on a base that lives in a referenced assembly.
/// </summary>
/// <remarks>
///     <para>
///         The attribute was documented as the way to override an error's status, and nothing
///         read it there — the generator read it on the <b>endpoint</b>, as the success status, and an error
///         decorated with it was answered with whatever its own <c>StatusCode</c> said.
///     </para>
///     <para>
///         It also closes what reading the syntax has to leave open. A property value is not metadata, so a status a
///         referenced base declares under a name of its own is invisible to the build; an attribute <b>is</b>
///         metadata, and is the one form the build can always read.
///     </para>
/// </remarks>
public class AnErrorDeclaringItsHttpStatusTests : EndpointsGeneratorTestBase
{
    private static string Endpoint(string error) => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        using Pragmatic.Result.Http;

        namespace Test.Api;

        public class NoteDto { public string Text { get; set; } = ""; }

        {{error}}

        [Endpoint(HttpVerb.Get, "/api/notes")]
        public partial class GetNotesEndpoint : Endpoint<NoteDto, TheError>
        {
            public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
        }
        """;

    /// <summary>A base whose status is a property of its own name: unreadable by the build, unless declared.</summary>
    private const string SharedErrors = """
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace Shared.Errors;

        [HttpStatus(410)]
        public abstract record GoneError : Error
        {
            protected static int Gone => 410;

            public override string Code => "GONE";
            public override int StatusCode => Gone;
        }
        """;

    /// <remarks>
    ///     ⚠️ The error's own <c>StatusCode</c> is a constant reference rather than a literal, so the syntax
    ///     walk cannot fold it: only the attribute can answer 402 here. Written as a literal, this test
    ///     would pass without the attribute being read, for the wrong reason.
    /// </remarks>
    [Fact]
    public void AnErrorCarryingIt_IsDocumentedWithTheDeclaredStatus()
        => DocumentedStatusOf(RunGenerator(Endpoint("""
            [HttpStatus(402)]
            public sealed record TheError : Error
            {
                private const int PaymentRequired = 402;

                public override string Code => "PAYMENT_REQUIRED";
                public override int StatusCode => PaymentRequired;
            }
            """))).Should().Be(402);

    /// <summary>
    ///     The case reading the syntax cannot close: the base is in a referenced assembly and its status is not a
    ///     literal, so only the attribute can carry it — and it carries it identically as a DLL and as a
    ///     compilation.
    /// </summary>
    [Fact]
    public void AnAttributeOnABaseInAReferencedAssembly_IsRead_AsDllAndAsCompilation()
    {
        var asCompilation = GeneratorTestHelper.CompileReference(
            "Shared.Errors", SharedErrors,
            GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Error)),
            GeneratorTestHelper.FromTypeAssembly(typeof(HttpStatusAttribute)));
        var asDll = Emitted((CompilationReference)asCompilation);
        var source = Endpoint("public sealed record TheError : Shared.Errors.GoneError;");

        DocumentedStatusOf(RunGenerator(source, asDll)).Should().Be(410);
        DocumentedStatusOf(RunGenerator(source, asCompilation)).Should().Be(410,
            "the editor and the build write one contract");
    }

    [Fact]
    public void AnErrorWhoseDeclaredStatusContradictsItsOwn_IsReported()
    {
        var result = RunGenerator(Endpoint("""
            [HttpStatus(410)]
            public sealed record TheError : Error
            {
                public override string Code => "GONE";
                public override int StatusCode => 404;
            }
            """));

        HasDiagnostic(result, "PRAG0537").Should()
            .BeTrue("the document says 410 and the runtime answers 404, and a reader cannot tell which is true");
    }

    /// <summary>The control: without the attribute nothing changes, and nothing is reported.</summary>
    [Fact]
    public void AnErrorWithoutIt_IsDocumentedAsBefore()
    {
        var result = RunGenerator(Endpoint("""
            public sealed record TheError : BusinessRuleError
            {
                public TheError() : base("worksite-closed") { }
            }
            """));

        DocumentedStatusOf(result).Should().Be(422);
        HasDiagnostic(result, "PRAG0537").Should().BeFalse();
    }

    /// <summary>The status the generated endpoint declares for <c>TheError</c> to the API explorer.</summary>
    private static int DocumentedStatusOf(SourceGenRunResult result)
    {
        const string declared = "ProducesResponseTypeMetadata(";
        const string ofTheError = ", typeof(global::Test.Api.TheError))";

        var sources = GetGeneratedSourcesAsDictionary(result);
        var endpoint = sources.Values.FirstOrDefault(s => s.Contains(ofTheError, StringComparison.Ordinal));
        endpoint.Should().NotBeNull(
            $"the endpoint declares a response for TheError; generated: {string.Join(", ", sources.Keys)}");

        var end = endpoint!.IndexOf(ofTheError, StringComparison.Ordinal);
        var start = endpoint.LastIndexOf(declared, end, StringComparison.Ordinal) + declared.Length;
        return int.Parse(endpoint[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static MetadataReference Emitted(CompilationReference reference)
    {
        using var image = new MemoryStream();
        var emitted = reference.Compilation.Emit(image);
        emitted.Success.Should().BeTrue(string.Join("\n", emitted.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }
}
