using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     An operation that carries a file and an ordinary property compiles, and the property
///     arrives from the form.
/// </summary>
/// <remarks>
///     <para>
///         A file makes the request <c>multipart/form-data</c>, so the handler binds the file as its own
///         parameter and emits <b>no</b> JSON body parameter. Reading the other properties from
///         <c>body</c>, the shape emitted for a JSON operation, would name a variable nobody declared:
///         <c>error CS0103: The name 'body' does not exist in the current context</c>, inside a file
///         the author cannot edit, whose workaround is an attribute the message does not mention.
///     </para>
///     <para>
///         The shape is Casework's <c>UploadALetterTemplateAction</c>: a <c>.pdxdoc</c> plus a
///         <c>Piece</c> saying which part of the letter it is, with no attribute on <c>Piece</c>.
///     </para>
///     <para>
///         ⚠️ <b>The generator knows the answer</b>, which is why this is binding and not a diagnostic:
///         a request that carries a file has no JSON body for a property to have come from, and route,
///         header, claim and query values are already bound from their own sources. What is left is a
///         form field, and what can be decided at compile time is decided there: the generator emits the line it knows rather than asking the
///         author to repeat it. What a multipart request genuinely cannot carry is a nested object —
///         and that is <c>PRAG0552</c>, the mirror of <c>PRAG0532</c> for a query string.
///     </para>
/// </remarks>
public class AFileBesideAPropertyNobodyMarkedTests : EndpointsGeneratorTestBase
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.Letters;
        """;

    /// <summary>The shape from Casework: a file, and a property nobody marked.</summary>
    private const string DomainAction = Usings + """

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/letters/templates")]
        public partial class UploadALetterTemplateAction : DomainAction<Guid>
        {
            [FromForm]
            public required IFormFile File { get; init; }

            public required string Piece { get; init; }

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    private const string Mutation = Usings + """

        public class Letter : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Piece { get; private set; } = "";
        }

        [Mutation(Mode = MutationMode.Update)]
        [Endpoint(HttpVerb.Put, "api/letters/{id}/template")]
        public partial class ReplaceTheTemplateMutation : Mutation<Letter>
        {
            public required Guid Id { get; init; }

            [FromForm]
            public required IFormFile File { get; init; }

            public required string Piece { get; init; }
        }
        """;

    private const string Endpoint = Usings + """

        public record UploadResult(string Piece, long Bytes);

        [Endpoint(HttpVerb.Post, "api/letters/raw")]
        public partial class UploadRawTemplateEndpoint : Endpoint<UploadResult>
        {
            [FromForm]
            public required IFormFile File { get; init; }

            public required string Piece { get; init; }

            public override Task<Result<UploadResult>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<UploadResult>>(new UploadResult(Piece, File.Length));
        }
        """;

    /// <summary>An operation whose unmarked property is a nested object a form field cannot carry.</summary>
    private const string WithANestedProperty = Usings + """

        public record Provenance(string Author, DateTimeOffset Written);

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/letters/provenance")]
        public partial class UploadWithProvenanceAction : DomainAction<Guid>
        {
            [FromForm]
            public required IFormFile File { get; init; }

            public required Provenance Provenance { get; init; }

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    /// <summary>The control: with no file, an ordinary property still comes from the JSON body.</summary>
    private const string NoFile = Usings + """

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/letters/notes")]
        public partial class AddANoteAction : DomainAction<Guid>
        {
            public required string Piece { get; init; }

            public required string Note { get; init; }

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    /// <summary>
    ///     The error this defect produces. The test references are a deliberate subset of what a
    ///     consumer compiles against, so the whole compilation is never clean here — what must be
    ///     absent is a name the generated file invented.
    /// </summary>
    private static string UndeclaredNames(SourceGenRunResult result)
        => string.Join("\n", GetCompilationErrors(result)
            .Where(e => e.Id == "CS0103"
                        && (e.GetMessage().Contains("'body'")
                            // ⚠️ And a validator for a …Body record nobody emits: the second copy of
                            // "what is a body property" lives in the Validation feature, and every rule
                            // this one learns has to reach it too.
                            || e.Location.SourceTree?.FilePath.Contains("Body") == true))
            .Select(e => e.ToString()));

    [Fact]
    public void ADomainActionWithAFileAndAPlainProperty_Compiles()
    {
        var result = RunGenerator(DomainAction);

        UndeclaredNames(result).Should().BeEmpty();

        var handler = GetGeneratedSource(result, "UploadALetterTemplateAction.Endpoint")!;
        handler.Should().Contain("Piece = piece",
            "the property comes from the form, like everything else in a multipart request");
        handler.Should().NotContain("body.Piece",
            "there is no body parameter in a multipart handler, which is the whole defect");
    }

    [Fact]
    public void AMutationWithAFileAndAPlainProperty_Compiles()
    {
        var result = RunGenerator(Mutation);

        UndeclaredNames(result).Should().BeEmpty();
        GetGeneratedSource(result, "ReplaceTheTemplateMutation.Endpoint")!
            .Should().NotContain("body.Piece");
    }

    [Fact]
    public void AnEndpointWithAFileAndAPlainProperty_Compiles()
    {
        var result = RunGenerator(Endpoint);

        UndeclaredNames(result).Should().BeEmpty();
        GetGeneratedSource(result, "UploadRawTemplateEndpoint.Endpoint")!
            .Should().NotContain("body.Piece");
    }

    /// <summary>
    ///     What a multipart request cannot carry is said, not bound: a nested object has no form-field
    ///     shape, so the property is named and the reader is told what to do.
    /// </summary>
    [Fact]
    public void APropertyAFormFieldCannotCarry_IsReported()
    {
        var result = RunGenerator(WithANestedProperty);

        HasDiagnostic(result, "PRAG0552").Should().BeTrue(
            "the property is named rather than silently dropped or emitted as an undeclared variable");
    }

    /// <summary>
    ///     The control that keeps this from turning every operation's properties into form fields: with
    ///     no file there is no multipart request, and the body is still the body.
    /// </summary>
    [Fact]
    public void WithNoFile_ThePropertiesStillComeFromTheBody()
    {
        var result = RunGenerator(NoFile);

        var handler = GetGeneratedSource(result, "AddANoteAction.Endpoint")!;
        handler.Should().Contain("body.Piece",
            "a JSON operation reads its values from the body, and that is not what changed");
        HasDiagnostic(result, "PRAG0552").Should().BeFalse();
    }
}
