using System.IO;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     An endpoint's error documents the status its base declares, wherever the base lives, and the IDE
///     and the build document the same one.
/// </summary>
/// <remarks>
///     <para>
///         The status was read from a <c>StatusCode</c> literal in the error's syntax, walking up
///         its bases; a base in a referenced assembly is metadata, so the walk found nothing and the
///         fallback guessed from the error's <b>own</b> name. <c>BusinessRuleError</c> is unsealed so that
///         each rule gets a type of its own — and every such type was documented 400 instead of 422.
///     </para>
///     <para>
///         In an IDE a referenced project is a compilation, its syntax is there, and the walk read it: the
///         editor and the build wrote different contracts from the same code.
///     </para>
/// </remarks>
public class AnInheritedErrorStatusIsReadFromItsBaseTests : EndpointsGeneratorTestBase
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

    private const string SharedErrors = """
        using Pragmatic.Result;

        namespace Shared.Errors;

        public abstract record GoneError : Error
        {
            public override string Code => "GONE";
            public override int StatusCode => 410;
        }
        """;

    [Fact]
    public void ARuleDerivedFromBusinessRuleError_Documents422()
        => DocumentedStatusOf(RunGenerator(Endpoint("""
            public sealed record TheError : BusinessRuleError
            {
                public TheError() : base("worksite-closed") { }
            }
            """))).Should().Be(422);

    /// <summary>The control: an error that declares its own status keeps it.</summary>
    [Fact]
    public void AnErrorDeclaringItsStatus_KeepsIt()
        => DocumentedStatusOf(RunGenerator(Endpoint("""
            public sealed record TheError : Error
            {
                public override string Code => "TEAPOT";
                public override int StatusCode => 418;
            }
            """))).Should().Be(418);

    /// <summary>
    ///     A base of the application's in a referenced project: as a compilation (the IDE) and as a DLL
    ///     (the build) the generated endpoint is the same.
    /// </summary>
    [Fact]
    public void ABaseInAReferencedProject_IsDocumentedTheSameInTheIdeAndInTheBuild()
    {
        var asCompilation = GeneratorTestHelper.CompileReference(
            "Shared.Errors", SharedErrors,
            GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Error)));
        var asDll = Emitted((CompilationReference)asCompilation);
        var source = Endpoint("""
            public sealed record TheError : Shared.Errors.GoneError;
            """);

        var inTheIde = DocumentedStatusOf(RunGenerator(source, asCompilation));
        var inTheBuild = DocumentedStatusOf(RunGenerator(source, asDll));

        inTheIde.Should().Be(inTheBuild, "the editor and the build write one contract");
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
