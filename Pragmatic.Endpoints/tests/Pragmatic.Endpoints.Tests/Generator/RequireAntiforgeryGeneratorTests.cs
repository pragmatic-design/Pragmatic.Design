using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     [RequireAntiforgery] emits RequireAntiforgeryTokenAttribute metadata and suppresses the
///     default DisableAntiforgery() on form endpoints; form endpoints without the attribute
///     keep the historical DisableAntiforgery() (regression guard).
/// </summary>
public class RequireAntiforgeryGeneratorTests : EndpointsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        """;

    [Fact]
    public void RequireAntiforgery_OnJsonEndpoint_EmitsMetadata()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/notes")]
            [RequireAntiforgery]
            public partial class CreateNoteEndpoint : Endpoint<NoteDto>
            {
                public required string Text { get; set; }

                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "CreateNoteEndpoint.Endpoint");
        generated.Should().Contain(".WithMetadata(new Microsoft.AspNetCore.Antiforgery.RequireAntiforgeryTokenAttribute());");
        generated.Should().NotContain("DisableAntiforgery");
    }

    [Fact]
    public void RequireAntiforgery_OnFormEndpoint_SkipsDisableAntiforgery()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class UploadDto { public string FileName { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/uploads")]
            [RequireAntiforgery]
            public partial class UploadEndpoint : Endpoint<UploadDto>
            {
                [FromForm]
                public string Title { get; set; } = "";

                public override Task<Result<UploadDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<UploadDto>.Success(new UploadDto()));
            }
            """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "UploadEndpoint");
        generated.Should().Contain("RequireAntiforgeryTokenAttribute");
        generated.Should().NotContain("DisableAntiforgery");
    }

    [Fact]
    public void FormEndpoint_WithoutAttribute_KeepsDisableAntiforgery()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class UploadDto { public string FileName { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/uploads")]
            public partial class UploadEndpoint : Endpoint<UploadDto>
            {
                [FromForm]
                public string Title { get; set; } = "";

                public override Task<Result<UploadDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<UploadDto>.Success(new UploadDto()));
            }
            """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "UploadEndpoint");
        generated.Should().Contain("DisableAntiforgery");
        generated.Should().NotContain("RequireAntiforgeryTokenAttribute");
    }
}
