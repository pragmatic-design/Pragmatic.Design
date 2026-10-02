using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     [MaxBodySize] emits MaxBodySizeMetadata (enforced by RequestLimitsStep at runtime)
///     and flows into the manifest; non-positive limits are rejected with PRAG0517.
/// </summary>
public class MaxBodySizeGeneratorTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void MaxBodySize_OnEndpoint_EmitsMetadataAndManifest()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/notes")]
            [MaxBodySize(1024)]
            public partial class CreateNoteEndpoint : Endpoint<NoteDto>
            {
                public string Text { get; set; } = "";

                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);


        HasDiagnostic(result, "PRAG0517").Should().BeFalse();

        var generated = GetGeneratedSource(result, "CreateNoteEndpoint");
        generated.Should().Contain(".WithMetadata(new global::Pragmatic.Http.MaxBodySizeMetadata(1024L));");

        var manifest = GetGeneratedSource(result, "_Metadata.PragmaticManifest");
        manifest.Should().Contain("\"maxBodySizeBytes\": 1024");
    }

    [Fact]
    public void MaxBodySize_NonPositive_ReportsPrag0517AndSuppressesEmission()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            [Endpoint(HttpVerb.Post, "/api/notes")]
            [MaxBodySize(0)]
            public partial class CreateNoteEndpoint : Endpoint<NoteDto>
            {
                public string Text { get; set; } = "";

                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0517").Should().BeTrue();
        GetGeneratedSource(result, "CreateNoteEndpoint").Should().NotContain("MaxBodySizeMetadata");
    }

    [Fact]
    public void MaxBodySize_OnMutation_EmitsMetadata()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;

            namespace Test.Api;

            public class Guest
            {
                public System.Guid Id { get; set; }
                public string Name { get; set; } = "";
            }

            [Endpoint(HttpVerb.Post, "/api/guests")]
            [MaxBodySize(2048)]
            public partial class CreateGuestMutation : Mutation<Guest>
            {
                public string Name { get; set; } = "";

                protected override void Apply(Guest entity) => entity.Name = Name;
            }
            """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "CreateGuestMutation");
        generated.Should().NotBeNull();
        generated.Should().Contain("MaxBodySizeMetadata(2048L)");
    }
}
