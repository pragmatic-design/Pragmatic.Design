using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Cabling tests proving these Endpoints diagnostics are emitted, not only declared:
///     PRAG0505 (duplicate explicit endpoint name) and PRAG0507 (Group type is not an [EndpointGroup]).
///     A processor that implements neither processor interface needs no diagnostic of ours: the
///     attribute's type constraint makes it a compile error.
/// </summary>
public class OrphanDiagnosticsCablingTests : EndpointsGeneratorTestBase
{
    // ── A processor must implement the interface: the compiler refuses it ──

    [Fact]
    public void PreProcessor_TypeNotImplementingInterface_IsACompileError()
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

            // Does NOT implement IEndpointPreProcessor
            public class NotAProcessor { }

            [Endpoint(HttpVerb.Get, "/api/notes")]
            [PreProcessor<NotAProcessor>]
            public partial class GetNotesEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        // CS0311: the type argument does not satisfy `where TProcessor : IEndpointPreProcessor`.
        GetCompilationErrors(result).Select(e => e.Id).Should().Contain("CS0311");
    }

    /// <summary>The control: a processor that implements the interface compiles.</summary>
    [Fact]
    public void PreProcessor_ValidProcessor_Compiles()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Endpoints.Context;
            using Pragmatic.Endpoints.Processors;
            using Pragmatic.Result;

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            public class ValidPreProcessor : IEndpointPreProcessor
            {
                public ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct = default)
                    => new(PreProcessorResult.Continue());
            }

            [Endpoint(HttpVerb.Get, "/api/notes")]
            [PreProcessor<ValidPreProcessor>]
            public partial class GetNotesEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        GetCompilationErrors(result).Select(e => e.Id).Should().NotContain("CS0311");
    }

    // ── PRAG0507: Group must reference an [EndpointGroup] type ──

    [Fact]
    public void Group_TypeExistsButIsNotEndpointGroup_ReportsPrag0507()
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

            // A plain class — NOT decorated with [EndpointGroup]
            public class NotAGroup { }

            [Endpoint(HttpVerb.Get, "/api/notes")]
            [EndpointGroup<NotAGroup>]
            public partial class GetNotesEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0507").Should().BeTrue();
    }

    [Fact]
    public void Group_ValidEndpointGroup_DoesNotReportPrag0507()
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

            [EndpointGroup("/api/admin")]
            public class AdminGroup { }

            [Endpoint(HttpVerb.Get, "/api/notes")]
            [EndpointGroup<AdminGroup>]
            public partial class GetNotesEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0507").Should().BeFalse();
    }

    // ── PRAG0505: explicit endpoint Name must be unique ──

    [Fact]
    public void EndpointName_DuplicatedAcrossEndpoints_ReportsPrag0505()
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

            [Endpoint(HttpVerb.Get, "/api/a", Name = "Dup")]
            public partial class GetAEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }

            [Endpoint(HttpVerb.Get, "/api/b", Name = "Dup")]
            public partial class GetBEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0505").Should().BeTrue();
    }

    [Fact]
    public void EndpointName_DistinctNames_DoesNotReportPrag0505()
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

            [Endpoint(HttpVerb.Get, "/api/a", Name = "GetA")]
            public partial class GetAEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }

            [Endpoint(HttpVerb.Get, "/api/b", Name = "GetB")]
            public partial class GetBEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0505").Should().BeFalse();
    }

    [Fact]
    public void EndpointName_NoExplicitName_DoesNotReportPrag0505()
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

            [Endpoint(HttpVerb.Get, "/api/a")]
            public partial class GetAEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }

            [Endpoint(HttpVerb.Get, "/api/b")]
            public partial class GetBEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0505").Should().BeFalse();
    }
}
