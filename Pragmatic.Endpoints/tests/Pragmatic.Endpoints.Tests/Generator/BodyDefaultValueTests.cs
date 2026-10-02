using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     The generated request body keeps the initializer written on the action.
/// </summary>
/// <remarks>
///     <para>
///         Without it a non-required, non-nullable property with a default arrives as <c>null</c>
///         whenever the caller omits the field. The action's own type says it cannot be null and its
///         declaration says what it defaults to; it would get neither, and throw a
///         <c>NullReferenceException</c> from inside code that has every reason to trust the property.
///     </para>
///     <para>
///         The same model carries the answer to both surfaces: the flattened boundary overload emits
///         <c>IngestText(…, string slot = "", …)</c>, and the request body keeps the same default.
///     </para>
/// </remarks>
public class BodyDefaultValueTests : EndpointsGeneratorTestBase
{
    private const string Preamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Work;

        public enum Shade { Plain = 0, Bold = 1 }
        """;

    private static string Source(string properties) => $$"""
        {{Preamble}}

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/work/stories")]
        public partial class WriteStoryAction : DomainAction<Guid>
        {
            public required string Title { get; init; }
        {{properties}}

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    private static string Body(string properties)
    {
        var sources = GetGeneratedSourcesAsDictionary(RunGeneratorWithPersistence(Source(properties)));
        return sources.First(kv => kv.Key.Contains("RequestBody")).Value;
    }

    [Fact]
    public void ADefaultedStringProperty_KeepsItsInitializer()
    {
        Body("""    public string FilledSlots { get; init; } = "";""")
            .Should().Contain("""FilledSlots { get; init; } = "";""",
                "the caller omitting the field must get the declared default, not null");
    }

    [Fact]
    public void ADefaultedEnumProperty_KeepsItsInitializer_Qualified()
    {
        var body = Body("    public Shade Shade { get; init; } = Shade.Bold;");

        // Qualified, because the body lives in its own namespace: an unqualified member name would
        // not compile there. The extraction that produces it already handled this for the boundary
        // overload, which is why it is reused rather than rewritten.
        body.Should().Contain("Shade { get; init; } = global::TestApp.Work.Shade.Bold;");
    }

    [Fact]
    public void APropertyWithoutAnInitializer_GetsNone()
    {
        Body("    public string? Note { get; init; }")
            .Should().NotContain("Note { get; init; } =",
                "inventing a default would be as wrong as dropping one");
    }

    /// <summary>
    ///     An initializer the author resolved through a <c>using</c> has to survive the move into the
    ///     generated file, which has the action's namespace and none of its usings.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The enum sits in a second namespace on purpose. In the action's own namespace the copied
    ///         text resolves by accident and the defect is invisible, which is why the two cases above —
    ///         both single-namespace — passed while a consumer could not build.
    ///     </para>
    ///     <para>
    ///         The assertion is on the compilation, not on the text: the failure is a <c>CS0103</c>
    ///         inside a file the author cannot open, so a test that only reads the body would agree with
    ///         the generator about a name neither of them can bind.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AnInitializerNamingATypeFromAUsing_StillCompiles()
    {
        var result = RunGeneratorWithPersistence(SourceWithImportedEnum(
            """    public string Kind { get; init; } = nameof(Tone.Bold);"""));

        ErrorsInTheBody(result).Should().BeEmpty(
            "the initializer is copied into a file that has the action's namespace and not its usings");
    }

    /// <summary>The same shape, on the enum-typed property: qualification already covered this one.</summary>
    /// <remarks>
    ///     The control. It shares every ingredient with the case above — imported namespace, member
    ///     access, copied initializer — and differs only in the property's type, which is what decides
    ///     whether the extraction qualifies the name or copies it. Without it, "the initializer
    ///     compiles" would be satisfied by an extraction that only ever handled enums.
    /// </remarks>
    [Fact]
    public void AnEnumTypedPropertyFromAUsing_AlreadyCompiled()
    {
        var result = RunGeneratorWithPersistence(SourceWithImportedEnum(
            "    public Tone Tone { get; init; } = Tone.Bold;"));

        ErrorsInTheBody(result).Should().BeEmpty();
        Body(result).Should().Contain("= global::TestApp.Kinds.Tone.Bold;");
    }

    private static string SourceWithImportedEnum(string properties) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;
        using TestApp.Kinds;

        namespace TestApp.Kinds
        {
            public enum Tone { Plain = 0, Bold = 1 }
        }

        namespace TestApp.Work
        {
            [DomainAction]
            [Endpoint(HttpVerb.Post, "api/work/stories")]
            public partial class WriteStoryAction : DomainAction<Guid>
            {
                public required string Title { get; init; }
        {{properties}}

                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
        }
        """;

    private static string Body(SourceGenRunResult result)
        => GetGeneratedSourcesAsDictionary(result).First(kv => kv.Key.Contains("RequestBody")).Value;

    /// <summary>Errors reported inside the generated request body, and nowhere else.</summary>
    /// <remarks>
    ///     The whole compilation cannot be the subject: this test project references a deliberate
    ///     subset, so the endpoint and registration files report missing assemblies that have nothing
    ///     to do with the property under test. Asserting over all of them measures the reference list.
    /// </remarks>
    private static IReadOnlyList<string> ErrorsInTheBody(SourceGenRunResult result)
        => GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("RequestBody") == true)
            .Select(d => d.ToString())
            .ToList();
}
