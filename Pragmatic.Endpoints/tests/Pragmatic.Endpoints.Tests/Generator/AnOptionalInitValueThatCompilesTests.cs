using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     An optional header or query value on an <c>init</c> property has to compile, and an absent value
///     has to leave the property at what its declaration says.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Optional values go into the object initializer with the required ones. Assigned after it —
///         <c>if (source is not null) action.Source = source;</c> — they are CS8852 on an <c>init</c>
///         property, in a generated file, and <c>init</c> is how every other property of these classes
///         is written. An absent value still leaves a declared initializer in place, through
///         <c>?? &lt;the declared default&gt;</c> inside the initializer, as the query template does.
///     </para>
///     <para>
///         The same rule as the form fields in <see cref="AFormThatCompilesTests" />, for headers and
///         query values.
///     </para>
/// </remarks>
public class AnOptionalInitValueThatCompilesTests : EndpointsGeneratorTestBase
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.Notes;
        """;

    private const string DomainAction = Usings + """

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/notes/{id}")]
        public partial class AddNoteAction : VoidDomainAction
        {
            public required Guid Id { get; init; }

            public required string Text { get; init; }

            [FromHeader(Name = "X-Note-Source")]
            public string? Source { get; init; }

            [FromQuery]
            public int Limit { get; init; } = 20;

            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }
        """;

    private const string Mutation = Usings + """

        public class Note : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Text { get; private set; } = "";

            internal void SetText(string value) => Text = value;
        }

        [Mutation(Mode = MutationMode.Update)]
        [Endpoint(HttpVerb.Put, "api/notes/{id}")]
        public partial class EditNoteMutation : Mutation<Note>
        {
            public required Guid Id { get; init; }

            public required string Text { get; init; }

            [FromHeader(Name = "X-Note-Source")]
            public string? Source { get; init; }
        }
        """;

    private const string Endpoint = Usings + """

        public record NoteEcho(string? Source, int Limit);

        [Endpoint(HttpVerb.Get, "api/notes/echo")]
        public partial class EchoNoteEndpoint : Endpoint<NoteEcho>
        {
            [FromHeader(Name = "X-Note-Source")]
            public string? Source { get; init; }

            [FromQuery]
            public int Limit { get; init; } = 20;

            public override Task<Result<NoteEcho>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<NoteEcho>>(new NoteEcho(Source, Limit));
        }
        """;

    /// <summary>
    ///     The errors this defect produces, and no other: the test references are a deliberate subset of
    ///     what a consumer compiles against, so the whole compilation is never clean here.
    /// </summary>
    private static string Errors(SourceGenRunResult result)
        => string.Join("\n", GetCompilationErrors(result)
            .Where(e => e.Id is "CS8852" or "CS9035" or "CS0019" or "CS0266" or "CS0029")
            .Select(e => e.ToString()));

    [Fact]
    public void ADomainActionWithOptionalInitValues_Compiles_AndKeepsTheDeclaredDefault()
    {
        var result = RunGenerator(DomainAction);

        Errors(result).Should().BeEmpty();

        var handler = GetGeneratedSource(result, "AddNoteAction.Endpoint")!;
        handler.Should().Contain("Source = source ?? default!",
            "an absent header leaves the property at its default, as construction did");
        handler.Should().Contain("Limit = limit ?? 20",
            "an absent query value leaves the declared initializer in place");
        handler.Should().NotContain("action.Source = source;", "and nothing is assigned after construction");
    }

    [Fact]
    public void AMutationWithAnOptionalInitHeader_Compiles()
    {
        var result = RunGenerator(Mutation);

        Errors(result).Should().BeEmpty();
        GetGeneratedSource(result, "EditNoteMutation.Endpoint")!.Should().Contain("Source = source ?? default!");
    }

    [Fact]
    public void AnEndpointWithOptionalInitValues_Compiles()
    {
        var result = RunGenerator(Endpoint);

        Errors(result).Should().BeEmpty();
        GetGeneratedSource(result, "EchoNoteEndpoint.Endpoint")!.Should().Contain("Limit = limit ?? 20");
    }

    /// <summary>
    ///     An initializer the generated file cannot reproduce, on an <c>init</c> property, is reported
    ///     instead of being compiled into an error inside a generated file.
    /// </summary>
    [Fact]
    public void AnInitPropertyWithANonConstantDefault_ReportsPRAG0536()
    {
        var result = RunGenerator(DomainAction.Replace(
            "public int Limit { get; init; } = 20;",
            "public string Tag { get; init; } = Guid.NewGuid().ToString(\"N\");"));

        Errors(result).Should().BeEmpty("nothing that cannot compile is emitted");

        var diagnostic = GetDiagnosticsById(result, "PRAG0536").Should().ContainSingle().Subject;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("Tag");
    }

    /// <summary>
    ///     The control: a <c>set</c> property keeps the assignment after construction, which is the only
    ///     shape that leaves an initializer of any kind in place.
    /// </summary>
    [Fact]
    public void ASetPropertyWithANonConstantDefault_IsStillAssignedAfterConstruction()
    {
        var result = RunGenerator(DomainAction.Replace(
            "public int Limit { get; init; } = 20;",
            "public string Tag { get; set; } = Guid.NewGuid().ToString(\"N\");"));

        Errors(result).Should().BeEmpty();
        GetDiagnosticsById(result, "PRAG0536").Should().BeEmpty();
        GetGeneratedSource(result, "AddNoteAction.Endpoint")!.Should().Contain("if (tag is not null) action.Tag = tag;");
    }
}
