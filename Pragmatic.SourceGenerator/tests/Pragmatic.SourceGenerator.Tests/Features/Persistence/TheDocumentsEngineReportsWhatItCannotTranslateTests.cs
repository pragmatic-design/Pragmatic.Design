using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The documents engine's generation on sources in memory: each PRAG31xx diagnostic where it belongs, with its remedy; and a chain of every
///     translated form whose generated interceptors, plans, readers and writers compile without an error.
/// </summary>
public class TheDocumentsEngineReportsWhatItCannotTranslateTests
{
    private const string Model = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Pragmatic.Persistence.Documents;

        [Document("items")]
        public sealed partial class Item
        {
            [Key] public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Price { get; set; }
            public DateTimeOffset At { get; set; }
            public List<Part> Parts { get; set; } = [];
            public List<string> Tags { get; set; } = [];
            [Version] public int Version { get; set; }
        }

        [Embedded]
        public sealed partial class Part
        {
            public string Code { get; set; } = "";
            public int Count { get; set; }
        }
        """;

    [Fact]
    public void EveryTranslatedForm_CompilesAndIsIntercepted()
    {
        var result = Run(Model + """

            public static class Reads
            {
                public static async Task Run(DocSession session, Guid id, List<string> names, (DateTimeOffset At, Guid Id) cursor)
                {
                    await session.Query<Item>().Where((i, a) => i.Id == a, id).OrderByDescending(i => i.At).ThenBy(i => i.Id).Skip(1).Take(5).ToListAsync();
                    await session.Query<Item>().Where(i => i.Price > 10m && !(i.Name == null) || i.Name.StartsWith("x")).CountAsync();
                    await session.Query<Item>().CountAsync((i, list) => list.Contains(i.Name), names);
                    await session.Query<Item>().AnyAsync(i => i.Parts.Any(p => p.Count > 2 && p.Code.Contains("k")));
                    await session.Query<Item>().AnyAsync((i, t) => i.Tags.Any(x => x == t), "tag");
                    await session.Query<Item>().FirstOrDefaultAsync((i, a) => i.Id == a, id);
                    await session.Query<Item>().OrderBy(i => i.At).ThenBy(i => i.Id).After(cursor).Take(10).ToListAsync();
                    await session.Query<Item>().Where(i => i.Price > 1m).Select(i => new { i.Name, Total = i.Price * 2 }).ToListAsync();
                    await session.Query<Item>().MaxAsync(i => i.Price);
                    await session.Query<Item>().SumAsync(i => i.Price);
                    var chain = session.Query<Item>().Where(i => i.Parts.Any());
                    await chain.ToListAsync();
                }
            }
            """);

        Errors(result.Diagnostics).Should().BeEmpty("every form above is in the translated subset");
        GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString()).Should().BeEmpty("the generated interceptors, plans, readers and writers compile");
        result.Diagnostics.Count(d => d.Id == "PRAG3199").Should().Be(11, "one translated terminal per line above");
    }

    [Fact]
    public void AnExpressionOutsideTheSubset_IsPrag3101()
    {
        var result = Run(Model + """

            public static class Reads
            {
                public static Task<long> Run(DocSession session) => session.Query<Item>().CountAsync(i => i.Name.Length > 3).AsTask();
            }
            """);

        var errors = result.Diagnostics.Where(d => d.Id == "PRAG3101").ToList();
        errors.Count.Should().Be(1);
        errors[0].GetMessage().Should().Contain("i.Name.Length > 3 is not translated: one side of a comparison is a field of the document", "Length is not a field");
    }

    [Fact]
    public void ACapturedLocal_IsPrag3102WithTheTwoParameterForm()
    {
        var result = Run(Model + """

            public static class Reads
            {
                public static Task<long> Run(DocSession session, string name) => session.Query<Item>().CountAsync(i => i.Name == name).AsTask();
            }
            """);

        var errors = result.Diagnostics.Where(d => d.Id == "PRAG3102").ToList();
        errors.Count.Should().Be(1);
        errors[0].GetMessage().Should().Contain("'name'");
        errors[0].GetMessage().Should().Contain("two-parameter form");
    }

    [Fact]
    public void ADocumentTypeNotPartial_IsPrag3103()
    {
        var result = Run("""
            using System;
            using Pragmatic.Persistence.Documents;

            [Document("plain")]
            public sealed class Plain
            {
                [Key] public Guid Id { get; set; }
            }
            """);

        result.Diagnostics.Any(d => d.Id == "PRAG3103" && d.GetMessage().Contains("'Plain' must be partial", StringComparison.Ordinal))
            .Should().BeTrue("the generator adds the reader and writer to the type");
    }

    [Fact]
    public void APropertyOfNoDocumentKind_IsPrag3104()
    {
        var result = Run("""
            using System;
            using Pragmatic.Persistence.Documents;

            [Document("links")]
            public sealed partial class Link
            {
                [Key] public Guid Id { get; set; }
                public Uri Target { get; set; } = null!;
            }
            """);

        result.Diagnostics.Any(d => d.Id == "PRAG3104" && d.GetMessage().Contains("'Link.Target'", StringComparison.Ordinal))
            .Should().BeTrue("a Uri is not a document field");
    }

    [Fact]
    public void AChainHandedOverByAMethod_IsPrag3105()
    {
        var result = Run(Model + """

            public static class Reads
            {
                public static Task<long> Count(DocQuery<Item> items) => items.CountAsync().AsTask();
            }
            """);

        result.Diagnostics.Any(d => d.Id == "PRAG3105" && d.GetMessage().Contains("session.Query<T>()", StringComparison.Ordinal))
            .Should().BeTrue("the chain cannot be followed back to where it starts");
    }

    [Fact]
    public void AReadByKeyAlone_IsACosmosPointRead()
    {
        var result = Run(Model + """

            public static class Reads
            {
                public static async Task Run(DocSession session, Guid id)
                {
                    await session.Query<Item>().FirstOrDefaultAsync((i, a) => i.Id == a, id);
                    await session.Query<Item>().FirstOrDefaultAsync((i, a) => i.Id == a && i.Price > 1m, id);
                    await session.Query<Item>().Where((i, a) => i.Id == a, id).OrderBy(i => i.At).ToListAsync();
                }
            }
            """);

        Errors(result.Diagnostics).Should().BeEmpty();
        GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString()).Should().BeEmpty();
        var interceptors = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Single(s => s.Key == "_Infra.Documents.Interceptors.g.cs").Value;
        // The key alone (the partition key of a document that names none): the id for a GET. Another condition, or an ordering: a query.
        (interceptors.Split("static s => ").Length - 1).Should().Be(1);
        interceptors.Should().Contain("static s => ((global::System.Guid)");
    }

    private static IEnumerable<string> Errors(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString());

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(source, "Pragmatic.Persistence.Documents.Generated",
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.Mongo.MongoClient>(),
            // The JSON half Cosmos DB reads is written on System.Text.Json's writer and element.
            GeneratorTestHelper.TryGetAssemblyReference("System.Text.Json") ?? throw new InvalidOperationException("'System.Text.Json' does not resolve in the test process."));
}
