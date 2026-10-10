using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     An endpoint whose response type the generated writer cannot reproduce keeps the serializer, and PRAG0555
///     says so on the endpoint, with the part of the type that decided it.
/// </summary>
public class AResponseThatKeepsTheSerializerSaysWhyTests : EndpointsGeneratorTestBase
{
    private const string Endpoint = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace TestApp.Notes;

        [Endpoint(HttpVerb.Get, "/notes")]
        public partial class GetNote : Endpoint<NoteDto>
        {
            public override Task<Result<NoteDto, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<NoteDto, IError>.Success(new NoteDto()));
        }

        """;

    [Fact]
    public void AMemberTypedJsonElement_KeepsTheSerializer_AndIsReported()
    {
        var result = RunGenerator(Endpoint + "public sealed class NoteDto { public System.Text.Json.JsonElement Payload { get; init; } }");

        var reported = GetDiagnosticsById(result, "PRAG0555").ToList();
        reported.Should().ContainSingle();
        reported[0].GetMessage().Should().Contain("GetNote").And.Contain("JsonElement");

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().Contain("Results.Ok(success)");
        handler.Should().NotContain("GeneratedJsonResponse<");
    }

    /// <summary>
    ///     A member typed <c>object</c> no longer sends the response to the serializer (#130): the writer writes the
    ///     type, the member's value goes to the serializer, and nothing is reported.
    /// </summary>
    [Fact]
    public void AMemberTypedObject_IsWrittenByTheWriter_ThroughTheSerializer()
    {
        var result = RunGenerator(Endpoint + "public sealed class NoteDto { public object? Payload { get; init; } }");

        GetDiagnosticsById(result, "PRAG0555").Should().BeEmpty();
        GetGeneratedSource(result, "Endpoint").Should().Contain("GeneratedJsonResponse<global::TestApp.Notes.NoteDto>(success!, 200,");
        GetGeneratedSource(result, "Utf8ResponseWriters").Should().Contain("Utf8JsonValues.WriteUntyped(writer, v0, options);");
    }

    /// <summary>The control: a type the writer reproduces is written by it, and nothing is reported.</summary>
    [Fact]
    public void ATypeTheWriterReproduces_IsWrittenByIt_AndNothingIsReported()
    {
        var result = RunGenerator(Endpoint + "public sealed class NoteDto { public string Text { get; init; } = \"\"; }");

        GetDiagnosticsById(result, "PRAG0555").Should().BeEmpty();
        GetGeneratedSource(result, "Endpoint").Should().Contain("GeneratedJsonResponse<global::TestApp.Notes.NoteDto>(success!, 200,");
        GetGeneratedSource(result, "Utf8ResponseWriters").Should().Contain("Write_TestApp_Notes_NoteDto");
    }
}
