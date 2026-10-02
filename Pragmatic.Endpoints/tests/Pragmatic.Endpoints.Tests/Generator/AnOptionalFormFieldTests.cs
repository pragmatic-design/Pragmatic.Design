using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A form field declared nullable is optional: the generated endpoint does not refuse a form
///     without it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>TypeName.EndsWith("?")</c> cannot tell a nullable <em>reference</em> type: the type name
///         comes from <c>ToDisplayString(FullyQualifiedFormat)</c>, which renders <c>string?</c> as
///         <c>string</c> — the <c>?</c> of a reference type is an annotation, not part of the type.
///         Deciding on the name, <c>[FromForm] public string? Caption</c> would answer 400 "the value is
///         missing or malformed" when the form has no caption, as on the Showcase's photo upload. A
///         nullable value type (<c>int?</c>) keeps its <c>?</c>, which is why only reference types are
///         affected.
///     </para>
///     <para>
///         Nullability is read from the symbol, once, in the transform — as the query path does for a
///         verb without a body.
///     </para>
/// </remarks>
public class AnOptionalFormFieldTests : EndpointsGeneratorTestBase
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

        namespace TestApp.Photos;
        """;

    /// <summary>An optional caption, a required title, an optional attachment.</summary>
    private const string Fields = """

            [FromForm]
            public string? Caption { get; init; }

            [FromForm]
            public required string Title { get; init; }

            [FromForm]
            public IFormFile? Attachment { get; init; }
        """;

    private const string DomainAction = Usings + """

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/photos")]
        public partial class UploadPhotoAction : DomainAction<string>
        {
        """ + Fields + """

            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success(Caption ?? Title));
        }
        """;

    private const string Mutation = Usings + """

        public class Photo : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Title { get; private set; } = "";
            public string? Caption { get; private set; }

            internal void SetTitle(string value) => Title = value;
            internal void SetCaption(string? value) => Caption = value;
        }

        [Mutation(Mode = MutationMode.Update)]
        [Endpoint(HttpVerb.Put, "api/photos/{id}")]
        public partial class ReplacePhotoMutation : Mutation<Photo>
        {
            public required Guid Id { get; init; }
        """ + Fields + """

        }
        """;

    private const string Endpoint = Usings + """

        public record Uploaded(string Title);

        [Endpoint(HttpVerb.Post, "api/photos/raw")]
        public partial class UploadRawPhotoEndpoint : Endpoint<Uploaded>
        {
        """ + Fields + """

            public override Task<Result<Uploaded>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<Uploaded>>(new Uploaded(Caption ?? Title));
        }
        """;

    /// <summary>The errors a nullable field handed to a non-nullable slot, or the reverse, would produce.</summary>
    private static string Errors(SourceGenRunResult result)
        => string.Join("\n", GetCompilationErrors(result)
            .Where(e => e.Id is "CS1503" or "CS0029" or "CS0266" or "CS9035" or "CS8852")
            .Select(e => e.ToString()));

    public static TheoryData<string, string> Shapes => new()
    {
        { DomainAction, "UploadPhotoAction.Endpoint" },
        { Mutation, "ReplacePhotoMutation.Endpoint" },
        { Endpoint, "UploadRawPhotoEndpoint.Endpoint" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ANullableTextField_IsNotRefusedWhenAbsent(string source, string hint)
    {
        var result = RunGenerator(source);
        var handler = GetGeneratedSource(result, hint)!;

        Errors(result).Should().BeEmpty();
        handler.Should().NotContain("WriteAsync(httpContext, \"Caption\"", "the caption is optional");
        handler.Should().Contain("string? caption", "and it is handed on as what it is: maybe absent");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ANullableFile_IsNotRefusedWhenAbsent(string source, string hint)
    {
        var handler = GetGeneratedSource(RunGenerator(source), hint)!;

        handler.Should().NotContain("\"attachment\", \"a file is required\"", "the attachment is optional");
    }

    /// <summary>The control: a non-nullable field is still refused with 400 when absent.</summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void ANonNullableField_IsStillRefused(string source, string hint)
    {
        var handler = GetGeneratedSource(RunGenerator(source), hint)!;

        handler.Should().Contain("WriteAsync(httpContext, \"Title\", \"the value is missing or malformed\")");
    }
}
