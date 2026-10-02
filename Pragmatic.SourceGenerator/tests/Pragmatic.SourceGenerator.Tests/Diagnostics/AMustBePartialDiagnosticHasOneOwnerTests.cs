using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Pragmatic.SourceGenerator.Analyzers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Diagnostics;

/// <summary>
///     A non-partial type gets its "must be partial" diagnostic once, from the companion analyzer, with
///     the generator and the analyzer both running — as they do in every build, since the analyzer
///     travels in the generator's package.
/// </summary>
/// <remarks>
///     <para>
///         Each of these IDs is declared once, in <c>NotPartialDiagnosticDescriptors</c>, and not again
///         in the generator's feature. Roslyn treats two descriptors with one ID as one diagnostic:
///         suppressing it suppresses both, and the user sees the same defect twice. The analyzer owns
///         them because it reports on the declaration, where the "Make class partial" code fix acts —
///         the same treatment as PRAG0600, PRAG0602 and PRAG1100.
///     </para>
///     <para>
///         Counted over both producers, not over one: a test that ran only the generator would pass on
///         "no diagnostic at all", which is the regression this suite exists to catch.
///     </para>
/// </remarks>
public sealed class AMustBePartialDiagnosticHasOneOwnerTests
{
    private const string Validation = """
        using Pragmatic.Validation.Attributes;

        namespace TestApp;

        public record CreateUserRequest
        {
            [Required]
            public string Name { get; init; } = "";
        }
        """;

    private const string Mapping = """
        using Pragmatic.Mapping.Attributes;

        namespace TestApp;

        public class Source
        {
            public int Id { get; set; }
        }

        [MapFrom<Source>]
        public record Target
        {
            public int Id { get; init; }
        }
        """;

    private const string DomainAction = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Result;

        namespace TestApp.Orders;

        [DomainAction]
        public class NotPartialAction : DomainAction<string>
        {
            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success("done"));
        }
        """;

    private const string Boundary = """
        using Pragmatic.Actions.Attributes;

        namespace TestApp.Booking;

        [Boundary]
        public class BookingBoundary;
        """;

    private const string Endpoint = """
        using Pragmatic.Endpoints.Attributes;

        namespace TestApp;

        [Endpoint(HttpVerb.Get, "/api/things")]
        public class GetThings;
        """;

    private const string MessageHandler = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Messaging;
        using Pragmatic.Messaging.Attributes;

        namespace TestApp;

        public sealed record OrderConfirmed(int OrderId);

        [MessageHandler]
        public class OrderConfirmedHandler : IMessageHandler<OrderConfirmed>
        {
            public Task HandleAsync(OrderConfirmed message, MessageContext context, CancellationToken ct = default)
                => Task.CompletedTask;
        }
        """;

    private const string Caching = """
        using Pragmatic.Caching.Attributes;

        namespace TestApp;

        [Cacheable(Duration = "5m")]
        public class GetUser
        {
            public int UserId { get; init; }
        }
        """;

    private const string Configuration = """
        using Pragmatic.Configuration;

        namespace TestApp.Config;

        [Configuration]
        public class NonPartialOptions
        {
            public int Value { get; set; } = 42;
        }
        """;

    private const string Patch = """
        using Pragmatic.Patch.Attributes;

        namespace TestApp;

        public class Guest
        {
            public string FirstName { get; set; } = "";
        }

        [GeneratePatch<Guest>]
        public record UpdateGuestPatch;
        """;

    private const string Job = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Jobs;
        using Pragmatic.Jobs.Attributes;

        namespace TestApp;

        [Job]
        public class NonPartialJob : IJob
        {
            public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
        }
        """;

    private const string Query = """
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public sealed class ThingBoundary;

        [Entity]
        [BelongsTo<ThingBoundary>]
        public partial class Thing : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Query<Thing, Thing>]
        public class NotPartialQuery
        {
            public string? Name { get; init; }
        }
        """;

    public static TheoryData<string, string> NonPartialTypes => new()
    {
        { "PRAG0712", Query },
        { "PRAG0200", Validation },
        { "PRAG0300", Mapping },
        { "PRAG0400", DomainAction },
        { "PRAG0406", Boundary },
        { "PRAG0500", Endpoint },
        { "PRAG0801", MessageHandler },
        { "PRAG1700", Caching },
        { "PRAG2000", Configuration },
        { "PRAG2200", Patch },
        { "PRAG2502", Job },
    };

    [Theory]
    [MemberData(nameof(NonPartialTypes))]
    public void ANonPartialType_IsReportedOnce(string id, string source)
    {
        var diagnostics = DiagnosticsOf(source);

        diagnostics.Where(d => d.Id == id).Should().HaveCount(1,
            $"one defect, one diagnostic — {id} has one owner. Reported: "
            + string.Join("; ", diagnostics.Select(d => $"{d.Id} at {d.Location.GetLineSpan()}")));
    }

    /// <summary>
    ///     Where it is reported: on the type's declaration, which is where the code fix can act.
    /// </summary>
    [Theory]
    [MemberData(nameof(NonPartialTypes))]
    public void TheDiagnostic_IsOnTheDeclaration(string id, string source)
    {
        var diagnostics = DiagnosticsOf(source).Where(d => d.Id == id).ToList();

        diagnostics.Should().OnlyContain(d => d.Location.IsInSource,
            "a diagnostic outside the syntax tree has no lightbulb, so the code fix cannot run. Reported: "
            + string.Join("; ", diagnostics.Select(d => $"{d.Location.Kind} {d.Location.GetLineSpan()}")));
        diagnostics.Should().NotBeEmpty();
    }

    /// <summary>The control: the same declarations, partial, raise none of the ten.</summary>
    [Theory]
    [MemberData(nameof(NonPartialTypes))]
    public void APartialType_IsNotReported(string id, string source)
    {
        var partial = source
            .Replace("public class ", "public partial class ", StringComparison.Ordinal)
            .Replace("public record Target", "public partial record Target", StringComparison.Ordinal)
            .Replace("public record CreateUserRequest", "public partial record CreateUserRequest", StringComparison.Ordinal)
            .Replace("public record UpdateGuestPatch", "public partial record UpdateGuestPatch", StringComparison.Ordinal);

        DiagnosticsOf(partial).Where(d => d.Id == id).Should().BeEmpty();
    }

    /// <summary>
    ///     A non-partial handler or job gets its diagnostic and nothing else: the generator emits no
    ///     <c>partial</c> part of the type, which the compiler would refuse with CS0260 in a file the
    ///     author cannot open.
    /// </summary>
    [Theory]
    [InlineData("PRAG0801")]
    [InlineData("PRAG2502")]
    public void ANonPartialHandlerOrJob_GetsNoGeneratedPartOfIt(string id)
    {
        var source = id == "PRAG0801" ? MessageHandler : Job;

        CompiledWithTheGenerator(source).GetDiagnostics()
            .Where(d => d.Id == "CS0260").Should().BeEmpty(
                "a part the type cannot take is not generated; the one diagnostic says why");
    }

    /// <summary>
    ///     A non-partial <c>[Query&lt;T&gt;]</c> is a query, not an action: the analyzer mapped every
    ///     <c>QueryAttribute</c> to PRAG0400, "Action class must be partial", beside the generator's PRAG0712.
    /// </summary>
    [Fact]
    public void ANonPartialQuery_IsNotReportedAsAnAction()
        => DiagnosticsOf(Query).Where(d => d.Id == "PRAG0400").Should().BeEmpty();

    /// <summary>PRAG2502 is an error, like every other "must be partial": without it the job does not run.</summary>
    [Fact]
    public void PRAG2502_IsAnError()
        => DiagnosticsOf(Job).Single(d => d.Id == "PRAG2502").Severity.Should().Be(DiagnosticSeverity.Error);

    private static Compilation CompiledWithTheGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), "Source.cs")],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PragmaticSourceGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output;
    }

    /// <summary>The generator's diagnostics and the companion analyzer's, over one compilation.</summary>
    private static ImmutableArray<Diagnostic> DiagnosticsOf(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), "Source.cs")],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PragmaticSourceGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var analyzerDiagnostics = output
            .WithAnalyzers([new NotPartialClassAnalyzer()])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();

        return [.. generatorDiagnostics, .. analyzerDiagnostics];
    }

    /// <summary>Every assembly next to this suite, plus the shared framework.</summary>
    /// <remarks>
    ///     Taken wholesale: the generator's feature detection switches on which Pragmatic assemblies the
    ///     compilation references, and a hand-picked list would switch a feature off and make its row
    ///     pass for the wrong reason.
    /// </remarks>
    private static readonly Lazy<MetadataReference[]> References = new(() =>
    {
        var byName = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(object).Assembly.Location)! })
        foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            var name = Path.GetFileName(file);
            if (byName.ContainsKey(name)
                || name.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Pragmatic.SourceGenerator", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("testhost", StringComparison.OrdinalIgnoreCase))
                continue;

            // A native DLL has no metadata to reference.
            using (var pe = new PEReader(File.OpenRead(file)))
                if (!pe.HasMetadata)
                    continue;

            byName[name] = MetadataReference.CreateFromFile(file);
        }

        return [.. byName.Values];
    });
}
