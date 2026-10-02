using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A <c>[FromForm]</c> operation written the way every operation of this framework is written —
///     <c>required … init</c> properties — has to compile.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Three independent traps, and no declaration avoids all three. The form fields go into the
///         object initializer, as every other path does — route, header, query, body; assigned
///         <em>after</em> construction they are <c>CS9035</c> on a <c>required</c> property and
///         <c>CS8852</c> on an <c>init</c> one. A non-file form field of a type other than
///         <c>string</c> needs a typed binder: <c>TryBindString</c> handed to a parameter of its own
///         type is <c>CS1503</c> on a <c>Guid</c>. And the Validation feature, which keeps its own copy
///         of "what is a body property", has to know <c>[FromForm]</c>, or it generates a validator for
///         a <c>…Body</c> type the Endpoints feature never emits.
///     </para>
///     <para>
///         Form fields declared <c>{ get; set; } = null!</c>, as the Showcase's upload does, survive
///         all three, so that shape proves nothing.
///     </para>
///     <para>
///         The assertion is the compilation, on all three handler templates — each carries its own
///         copy of the binding — plus the two lines that say <em>how</em> it compiles: the field in the
///         initializer, and the typed binder.
///     </para>
/// </remarks>
public class AFormThatCompilesTests : EndpointsGeneratorTestBase
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
        using Pragmatic.Validation.Attributes;

        namespace TestApp.Corpora;
        """;

    /// <summary>The shape the issue was found on: a domain action, two required form fields, a typed one.</summary>
    private const string DomainAction = Usings + """

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/corpora")]
        [ApiSummary("Upload a corpus")]
        public partial class UploadCorpusAction : DomainAction<Guid>
        {
            [FromForm]
            public required IFormFile File { get; init; }

            [FromForm]
            [MinLength(1)]
            public required string Title { get; init; }

            [FromForm]
            public Guid FolderId { get; init; }

            [FromForm]
            public string? Notes { get; init; }

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(FolderId));
        }
        """;

    private const string Mutation = Usings + """

        public class Corpus : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Title { get; private set; } = "";

            internal void SetTitle(string value) => Title = value;
        }

        [Mutation(Mode = MutationMode.Update)]
        [Endpoint(HttpVerb.Put, "api/corpora/{id}/cover")]
        public partial class ReplaceCoverMutation : Mutation<Corpus>
        {
            public required Guid Id { get; init; }

            [FromForm]
            public required IFormFile Cover { get; init; }
        }
        """;

    private const string Endpoint = Usings + """

        public record UploadResult(string Title, long Bytes);

        [Endpoint(HttpVerb.Post, "api/corpora/raw")]
        public partial class UploadRawEndpoint : Endpoint<UploadResult>
        {
            [FromForm]
            public required IFormFile File { get; init; }

            [FromForm]
            public required string Title { get; init; }

            public override Task<Result<UploadResult>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<UploadResult>>(new UploadResult(Title, File.Length));
        }
        """;

    /// <summary>
    ///     The errors this defect produces, and no other. The test references are a deliberate subset
    ///     of what a consumer compiles against, so the whole compilation is never clean here; the
    ///     three failures have their own codes, and the fourth — a validator naming a member of a type
    ///     nobody generates — is a CS0103 in a file named after that type.
    /// </summary>
    private static string Errors(SourceGenRunResult result)
        => string.Join("\n", GetCompilationErrors(result)
            .Where(e => e.Id is "CS9035" or "CS8852" or "CS1503" or "CS0029"
                        || (e.Id == "CS0103" && e.Location.SourceTree?.FilePath.Contains("Body") == true))
            .Select(e => e.ToString()));

    [Fact]
    public void ADomainActionWithRequiredFormFields_Compiles()
    {
        var result = RunGenerator(DomainAction);

        Errors(result).Should().BeEmpty();

        var handler = GetGeneratedSource(result, "UploadCorpusAction.Endpoint")!;
        handler.Should().Contain("Title = title", "a required form field is set where a required property can be set");
        handler.Should().Contain("TryBind<global::System.Guid>", "a typed form field is bound as its own type");
        handler.Should().NotContain("action.Title = title;", "and no longer assigned after construction");
    }

    [Fact]
    public void AMutationWithARequiredFormFile_Compiles()
    {
        var result = RunGenerator(Mutation);

        Errors(result).Should().BeEmpty();
        GetGeneratedSource(result, "ReplaceCoverMutation.Endpoint")!.Should().Contain("Cover = cover");
    }

    [Fact]
    public void AnEndpointWithRequiredFormFields_Compiles()
    {
        var result = RunGenerator(Endpoint);

        Errors(result).Should().BeEmpty();
        GetGeneratedSource(result, "UploadRawEndpoint.Endpoint")!.Should().Contain("Title = title");
    }

    /// <summary>
    ///     The control: the validation the form field declares is not lost with the body it never had.
    /// </summary>
    /// <remarks>
    ///     Without it, "it compiles" would be satisfied by dropping the <c>…Body</c> validator and, with
    ///     it, the <c>[MinLength]</c> on <c>Title</c>. The rule has to run on the operation itself.
    /// </remarks>
    [Fact]
    public void TheFormFieldsRule_StillRunsOnTheOperation()
    {
        var result = RunGenerator(DomainAction);

        var validators = GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Validation") || kv.Key.Contains("Validator"))
            .ToList();

        validators.Should().NotBeEmpty("a [MinLength] on the operation produces a validator for it");
        validators.Should().NotContain(kv => kv.Key.Contains("UploadCorpusActionBody"),
            "there is no body type to validate: the fields come from the form");
    }
}
