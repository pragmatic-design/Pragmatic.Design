using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     <c>[FromForm(Name = "…")]</c> names the key the field is read from, and the key the documents
///     publish.
/// </summary>
/// <remarks>
///     ⚠️ It was read into the model and then ignored: every template bound a form field by its property
///     name, so <c>[FromForm(Name = "file_caption")] public string Caption</c> read <c>Caption</c>, and a
///     client sending <c>file_caption</c> was refused. The runtime description used the same key, so the
///     two agreed with each other and not with the declaration.
/// </remarks>
public class ADeclaredFormFieldNameTests : EndpointsGeneratorTestBase
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

        namespace TestApp.Albums;
        """;

    private const string Fields = """

            [FromForm(Name = "file_caption")]
            public required string Caption { get; init; }

            [FromForm(Name = "upload")]
            public required IFormFile Photo { get; init; }

            [FromForm(Name = "copies")]
            [Required]
            public int? Copies { get; init; }

            [FromForm]
            public required string Title { get; init; }
        """;

    private const string DomainAction = Usings + """

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/albums")]
        public partial class AddToAlbumAction : DomainAction<string>
        {
        """ + Fields + """

            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success(Caption));
        }
        """;

    private const string Mutation = Usings + """

        public class Album : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Title { get; private set; } = "";
            public string Caption { get; private set; } = "";
            public int? Copies { get; private set; }

            internal void SetTitle(string value) => Title = value;
            internal void SetCaption(string value) => Caption = value;
            internal void SetCopies(int? value) => Copies = value;
        }

        [Mutation(Mode = MutationMode.Update)]
        [Endpoint(HttpVerb.Put, "api/albums/{id}")]
        public partial class ReplaceAlbumMutation : Mutation<Album>
        {
            public required Guid Id { get; init; }
        """ + Fields + """

        }
        """;

    private const string Endpoint = Usings + """

        public record Added(string Caption);

        [Endpoint(HttpVerb.Post, "api/albums/raw")]
        public partial class AddToAlbumEndpoint : Endpoint<Added>
        {
        """ + Fields + """

            public override Task<Result<Added>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<Added>>(new Added(Caption));
        }
        """;

    private const string Description = "global::Pragmatic.Endpoints.ApiExplorer.PragmaticParameterDescription";
    private const string ParameterSource = "global::Pragmatic.Endpoints.ApiExplorer.PragmaticParameterSource";

    public static TheoryData<string, string> Shapes => new()
    {
        { DomainAction, "AddToAlbumAction.Endpoint" },
        { Mutation, "ReplaceAlbumMutation.Endpoint" },
        { Endpoint, "AddToAlbumEndpoint.Endpoint" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ADeclaredName_IsTheKeyTheFieldIsReadFrom(string source, string hint)
    {
        var handler = GetGeneratedSource(RunGenerator(source), hint)!;

        handler.Should().Contain("RequestValues.Form(__form, \"file_caption\")");
        handler.Should().Contain("RequestValues.File(__form, \"upload\")");
        handler.Should().NotContain("RequestValues.Form(__form, \"Caption\")");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ADeclaredName_IsTheKeyTheDocumentPublishes(string source, string hint)
    {
        var handler = GetGeneratedSource(RunGenerator(source), hint)!;

        handler.Should().Contain($"new {Description}(\"file_caption\", {ParameterSource}.Form, typeof(string), true)");
        handler.Should().Contain($"new {Description}(\"upload\", {ParameterSource}.FormFile,");
        // Required by validation, bound as optional: the key it is looked up by moves with the name.
        handler.Should().Contain($"new {Description}(\"copies\", {ParameterSource}.Form, typeof(int), true)");
    }

    /// <summary>The control: a field without a declared name is still read by its property name.</summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void AFieldWithoutAName_IsStillReadByItsPropertyName(string source, string hint)
    {
        var handler = GetGeneratedSource(RunGenerator(source), hint)!;

        handler.Should().Contain("RequestValues.Form(__form, \"Title\")");
    }
}
