using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     The generated request body is in the generated JSON context.
/// </summary>
/// <remarks>
///     <para>
///         It was not. The context covered mapping DTOs, message payloads and job arguments — everything
///         with a symbol to walk — and skipped the one shape that has none, because this generator is
///         what creates it. Measured on a lab app: 2 types covered, 5 request bodies not.
///     </para>
///     <para>
///         Nothing said so. Reflection filled the gap on a normal run, and the only configuration that
///         reveals it, <c>DisableReflectionFallback()</c>, is the one a real AOT publish uses: every POST
///         would have thrown at the first call, after a build that reported nothing.
///     </para>
///     <para>
///         The assertion that matters here is not the text — it is that the compilation has no errors.
///         The context writes an <c>UnsafeAccessor</c> per init-only property; if the shape it recorded
///         disagreed with the record it emitted, that is where it would show.
///     </para>
/// </remarks>
public class BodyJsonContextCoverageTests : EndpointsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        [assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]

        namespace TestApp.Work;

        public enum Shade { Plain = 0, Bold = 1 }

        public sealed class Slot
        {
            public string Name { get; set; } = "";
            public int Order { get; set; }
        }

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/work/stories")]
        public partial class WriteStoryAction : DomainAction<Guid>
        {
            public required string Title { get; init; }
            public Shade Shade { get; init; } = Shade.Bold;
            public List<Slot> Slots { get; init; } = new();

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    private static (string Context, SourceGenRunResult Result) Run()
    {
        var result = RunGeneratorWithPersistence(Source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var context = sources.FirstOrDefault(kv => kv.Key.Contains("Json.Context"));

        context.Value.Should().NotBeNull("the assembly opted into the generated JSON context");
        return (context.Value, result);
    }

    [Fact]
    public void TheRequestBody_IsCovered()
    {
        var (context, _) = Run();

        context.Should().Contain("global::TestApp.Work.WriteStoryActionBody",
            "without it the body deserializes by reflection, which a real AOT publish does not have");
    }

    [Fact]
    public void TheAction_IsNotCovered()
    {
        var (context, _) = Run();

        // The action never crosses the wire — the body does. Registering it would put a type with
        // injected services and a CancellationToken into the serializer's metadata for nothing.
        context.Should().NotContain("Create_TestApp_Work_WriteStoryAction(",
            "the action is not a serialization boundary");
    }

    [Fact]
    public void TheBodysOwnTypes_ComeWithIt()
    {
        var (context, _) = Run();

        context.Should().Contain("global::TestApp.Work.Slot", "a nested type is part of the body's shape")
            .And.Contain("TestApp.Work.Shade", "so is an enum property");
    }

    [Fact]
    public void APropertyThatIsNotInTheBody_DoesNotCostTheCoverage()
    {
        // A computed property is not a body property and has no business deciding whether the body is
        // covered. Walking the action as a whole would have deferred on it and dropped the body from the
        // context without a diagnostic — the failure would have surfaced as a 500 in production.
        var withComputed = Source.Replace(
            "public required string Title { get; init; }",
            """
            public required string Title { get; init; }
                public string Slug => Title.ToLowerInvariant();
            """);

        var sources = GetGeneratedSourcesAsDictionary(RunGeneratorWithPersistence(withComputed));
        var context = sources.FirstOrDefault(kv => kv.Key.Contains("Json.Context")).Value;

        context.Should().NotBeNull().And.Contain("global::TestApp.Work.WriteStoryActionBody");
        context.Should().NotContain("\"slug\"", "it never crosses the wire in either direction");
    }

    [Fact]
    public void TheGeneratedContext_Compiles()
    {
        var (_, result) = Run();

        // Scoped to the context's own file. This harness does not reference Pragmatic.Authorization, so
        // the generated PolicyRegistry cannot compile here — a pre-existing gap in the reference set,
        // present with or without this feature, and not something to hide behind a green assertion.
        var errors = GetCompilationErrors(result)
            .Where(d => d.Location.GetLineSpan().Path.Contains("Json.Context"))
            .ToList();

        errors.Should().BeEmpty(
            $"the context accesses the body's init-only properties by UnsafeAccessor: {string.Join("; ", errors)}");
    }
}
