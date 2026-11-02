using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>LocalizedString</c> member of an entity the documents engine persists is a field: the JSON text of culture → text EF Core's converter
///     writes (<c>LocalizedString.ToJson</c>), read back through <c>LocalizedString.FromJson</c>, on MongoDB and on Cosmos DB alike (#475).
///     Before, PRAG3104 refused it as "not a document field".
/// </summary>
/// <remarks>The bridge is declared by the types the generator recognises it by, as in <see cref="ALoadWithIncludeIsCompiledOrRefusedByItsEngineTests" />.</remarks>
public class ALocalizedStringIsADocumentFieldTests
{
    private const string DocumentsBridge = """
        namespace Pragmatic.Persistence.Documents
        {
            public static class PragmaticDocFilters { }
            public sealed class DocCompiledSpecification<T> { }
            public sealed class DocUnitOfWork { }
        }
        """;

    private const string Source = """
        using Pragmatic.Persistence.Entity;

        namespace Pragmatic.Internationalization.Types
        {
            public sealed class LocalizedString
            {
                public string ToJson() => "{}";
                public static LocalizedString FromJson(string? json) => new();
            }
        }

        namespace Probe.Leaflets
        {
            [Entity] public partial class Leaflet : IEntity { public Pragmatic.Internationalization.Types.LocalizedString Title { get; set; } = new(); }
        }
        """;

    [Fact]
    public void TheTitle_IsStoredAsItsJsonText()
    {
        var result = GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Source + DocumentsBridge, "Pragmatic.Persistence.Documents.Generated",
            [
                GeneratorTestHelper.FromType<EntityAttribute>(),
                GeneratorTestHelper.FromType<IEntity>(),
                GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
                GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>(),
            ]);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3104").Should().BeEmpty("a LocalizedString is a document field");
        var generated = string.Concat(GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Values);
        generated.Should().Contain(".Title.ToJson()", "the field is written as the JSON text EF Core's converter writes");
        generated.Should().Contain("global::Pragmatic.Internationalization.Types.LocalizedString.FromJson(", "and read back through FromJson");
    }
}
