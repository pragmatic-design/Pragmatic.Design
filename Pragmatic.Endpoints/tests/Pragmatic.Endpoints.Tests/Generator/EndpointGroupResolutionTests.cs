using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     How <c>[EndpointGroup&lt;TGroup&gt;]</c> resolves the type it names, and what PRAG0507 says in
///     each of the ways it can fail.
/// </summary>
/// <remarks>
///     <para>
///         The group is not looked up <em>by name</em> in the endpoint's own assembly. That lookup
///         fails two ways. A group that does not exist at all produces no PRAG0507 — the descriptor
///         titled "Endpoint group not found" is silent in exactly that case, and reports only a type
///         that exists without <c>[EndpointGroup]</c>. And a group declared in a referenced assembly
///         is "not found" too, so its prefix is dropped without a word: the endpoint answers at the
///         bare route, and the document publishes it there.
///     </para>
///     <para>
///         The attribute already carries the symbol. Reading it instead of re-finding it by name
///         removes both failures at once.
///     </para>
/// </remarks>
public class EndpointGroupResolutionTests : EndpointsGeneratorTestBase
{
    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        """;

    [Fact]
    public void Group_TypeDoesNotExist_ReportsPrag0507()
    {
        var source = Usings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            [Endpoint(HttpVerb.Get, "/api/notes")]
            [EndpointGroup<NoSuchGroup>]
            public partial class GetNotesEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0507").Should().BeTrue(
            "a group that does not exist is the case the descriptor is named after");
        GetDiagnosticsById(result, "PRAG0507").First().GetMessage()
            .Should().Contain("does not exist");
    }

    /// <summary>
    ///     The other failure keeps its id and gets a message that names it: the type is there, the
    ///     attribute is not.
    /// </summary>
    [Fact]
    public void Group_TypeExistsButIsNotAGroup_SaysSoInTheMessage()
    {
        var source = Usings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

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
        GetDiagnosticsById(result, "PRAG0507").First().GetMessage()
            .Should().Contain("[EndpointGroup]");
    }

    /// <summary>
    ///     A group declared in a referenced assembly is a group: its prefix is applied and nothing
    ///     is reported.
    /// </summary>
    [Fact]
    public void Group_DeclaredInAReferencedAssembly_PrefixIsAppliedAndNothingIsReported()
    {
        var shared = GeneratorTestHelper.CompileReference("Shared.Api", """
            using Pragmatic.Endpoints.Attributes;

            namespace Shared.Api;

            [EndpointGroup("/api/shared")]
            public sealed class SharedGroup { }
            """, GeneratorTestHelper.FromType<Attributes.EndpointAttribute>());

        var source = Usings + """

            namespace Test.Api;

            public class NoteDto { public string Text { get; set; } = ""; }

            [Endpoint(HttpVerb.Get, "/notes")]
            [EndpointGroup<Shared.Api.SharedGroup>]
            public partial class GetNotesEndpoint : Endpoint<NoteDto>
            {
                public override Task<Result<NoteDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<NoteDto>.Success(new NoteDto()));
            }
            """;

        var result = RunGenerator(source, shared);

        HasDiagnostic(result, "PRAG0507").Should().BeFalse("the type exists and carries [EndpointGroup]");
        var registration = GetGeneratedSourcesAsDictionary(result)
            .First(s => s.Key.Contains("Endpoints.Registration")).Value;
        registration.Should().Contain("MapGroup(\"/api/shared\")",
            "a group the endpoint cannot see by name was silently dropped, prefix and all");
    }
}
