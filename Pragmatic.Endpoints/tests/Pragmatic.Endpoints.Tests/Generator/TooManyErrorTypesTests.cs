using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     PRAG0503: an endpoint declaring more error types than the generated pipeline can carry.
/// </summary>
/// <remarks>
///     <para>
///         The descriptor announced a maximum of six and nothing enforced it: the reason
///         <c>TooManyErrors</c> existed, the branch reporting it existed, and no transform ever assigned
///         it. A guard that cannot fire is indistinguishable from one that works, so the test declares
///         the seventh error and asks.
///     </para>
///     <para>
///         The shipped bases stop at six, so the seven-error base has to be declared by the test itself,
///         in the namespace the transform recognises. That is the point: the limit belongs to the
///         generator, not to whichever arities a package happens to ship today.
///     </para>
/// </remarks>
public class TooManyErrorTypesTests : EndpointsGeneratorTestBase
{
    private const string Errors = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace Test.Api;

        public class NoteDto { public string Text { get; set; } = ""; }

        public record E1 : IError { public string Code => "e1"; public int StatusCode => 400; }
        public record E2 : IError { public string Code => "e2"; public int StatusCode => 400; }
        public record E3 : IError { public string Code => "e3"; public int StatusCode => 400; }
        public record E4 : IError { public string Code => "e4"; public int StatusCode => 400; }
        public record E5 : IError { public string Code => "e5"; public int StatusCode => 400; }
        public record E6 : IError { public string Code => "e6"; public int StatusCode => 400; }
        public record E7 : IError { public string Code => "e7"; public int StatusCode => 400; }
        """;

    [Fact]
    public void SevenErrorTypes_ReportsPrag0503()
    {
        var source = Errors + """

            namespace Pragmatic.Endpoints.Base
            {
                public abstract class Endpoint<TResponse, TError1, TError2, TError3, TError4, TError5, TError6, TError7>
                {
                    public abstract Task<Result<TResponse>> HandleAsync(CancellationToken ct = default);
                }
            }

            namespace Test.Api
            {
                [Endpoint(HttpVerb.Get, "/api/notes")]
                public partial class GetNotesEndpoint : Endpoint<NoteDto, E1, E2, E3, E4, E5, E6, E7>
                {
                    public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                        => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0503").Should().BeTrue("seven error types exceed the announced maximum of six");
        GetDiagnosticsById(result, "PRAG0503").First().GetMessage()
            .Should().Contain("7 error types");
    }

    /// <summary>The control: six is the announced maximum, and six is accepted.</summary>
    [Fact]
    public void SixErrorTypes_DoesNotReportPrag0503()
    {
        var source = Errors + """

            [Endpoint(HttpVerb.Get, "/api/notes")]
            public partial class GetNotesEndpoint : Endpoint<NoteDto, E1, E2, E3, E4, E5, E6>
            {
                public override Task<Result<NoteDto, E1, E2, E3, E4, E5, E6>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto, E1, E2, E3, E4, E5, E6>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0503").Should().BeFalse("six is the maximum, not one past it");
        GetGeneratedSource(result, "GetNotesEndpoint.Endpoint").Should().NotBeNull(
            "a valid endpoint gets its handler");
    }
}
