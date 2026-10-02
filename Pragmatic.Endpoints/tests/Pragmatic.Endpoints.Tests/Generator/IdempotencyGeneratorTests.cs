using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     [Idempotent] emits IdempotencyEndpointFilter registration (with the body-hash provider),
///     documents the header in the manifest, and warns (PRAG0513) on safe verbs.
/// </summary>
public class IdempotencyGeneratorTests : EndpointsGeneratorTestBase
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
    public void Idempotent_OnPostWithBody_EmitsFilterWithBodyHash()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/notes")]
            [Idempotent(DurationSeconds = 600)]
            public partial class CreateNoteEndpoint : Endpoint<NoteDto>
            {
                public required string Text { get; set; }

                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0513").Should().BeFalse();
        var generated = GetGeneratedSource(result, "CreateNoteEndpoint.Endpoint");
        generated.Should().Contain("IdempotencyEndpointFilter(");
        generated.Should().Contain("600,");
        // No hash provider is emitted: the filter hashes the raw request body itself. A lambda reading
        // the bound body DTO out of the filter's arguments would see an empty list, because a
        // generated endpoint is a RequestDelegate that binds its own parameters — every request would
        // hash alike, and two different bodies under one key would replay instead of re-executing.
        generated.Should().NotContain("OfType<global::Test.Api.CreateNoteEndpointBody>");
        generated.Should().Contain("IdempotencyEndpointFilter(");

        var manifest = GetGeneratedSource(result, "_Metadata.PragmaticManifest");
        manifest.Should().Contain("\"idempotencyHeader\": \"Idempotency-Key\"");
    }

    [Fact]
    public void Idempotent_CustomHeader_FlowsIntoFilterAndManifest()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/notes")]
            [Idempotent(HeaderName = "X-Request-Id")]
            public partial class CreateNoteEndpoint : Endpoint<NoteDto>
            {
                public required string Text { get; set; }

                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        GetGeneratedSource(result, "CreateNoteEndpoint.Endpoint").Should().Contain("\"X-Request-Id\",");
        GetGeneratedSource(result, "_Metadata.PragmaticManifest")
            .Should().Contain("\"idempotencyHeader\": \"X-Request-Id\"");
    }

    [Fact]
    public void Idempotent_OnGet_ReportsPrag0513AndSkipsEmission()
    {
        var source = CommonUsings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            [Endpoint(HttpVerb.Get, "/api/notes")]
            [Idempotent]
            public partial class GetNotesEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0513").Should().BeTrue();
        GetGeneratedSource(result, "GetNotesEndpoint.Endpoint")
            .Should().NotContain("IdempotencyEndpointFilter");
    }
}
