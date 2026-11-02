using Microsoft.CodeAnalysis;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     An <c>[Autocomplete]</c> on an entity an engine persists: the engine's repository compiles one read per property
///     (<c>IAutocompleteReadRepository</c>), and the endpoint reads through it, not through EF Core's <c>Query()</c> and <c>ToListAsync</c>, which a
///     compilation without EF Core does not have.
/// </summary>
/// <remarks>
///     The bridges are declared by the types the generator recognises them by, as in <see cref="ALoadWithIncludeIsCompiledOrRefusedByItsEngineTests" />.
///     The generated text is asserted; the read itself runs in <c>Pragmatic.Persistence.Boundaries.Tests</c>.
/// </remarks>
public class AnAutocompleteIsCompiledByItsEngineTests
{
    private const string SqlBridge = """
        namespace Pragmatic.Persistence.Sql
        {
            public class SqlSession { }
            public static class PragmaticSqlFilters { }
            public class CompiledSpecification<T> { }
            public class SqlUnitOfWork { }
        }
        """;

    private const string DocumentsBridge = """
        namespace Pragmatic.Persistence.Documents
        {
            public static class PragmaticDocFilters { }
            public sealed class DocCompiledSpecification<T> { }
            public sealed class DocUnitOfWork { }
        }
        """;

    private const string Entity = """
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Ac
        {
            [Entity] public partial class Supplier : IEntity { [Autocomplete] public string Name { get; set; } = ""; }
        }
        """;

    [Fact]
    public void OnTheSqlEngine_TheEndpointReadsThroughTheCompiledRead()
    {
        var result = Run(Entity + SqlBridge, "Pragmatic.Persistence.Sql.Generated");
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        var endpoint = sources.Single(kv => kv.Key.Contains("SupplierNameAutocompleteEndpoint")).Value;
        endpoint.Should().NotContain("Microsoft.EntityFrameworkCore", "the compilation has no EF Core: the engine compiles the read");
        endpoint.Should().Contain("global::Pragmatic.Persistence.Repository.IAutocompleteReadRepository<global::Probe.Ac.Supplier>");
        endpoint.Should().Contain("AutocompleteAsync(\"Name\", search,");

        var repository = sources["Probe.Ac.Supplier.SqlReadRepository.g.cs"];
        repository.Should().Contain("global::Pragmatic.Persistence.Repository.IAutocompleteReadRepository<global::Probe.Ac.Supplier>");
        repository.Should().Contain("case \"Name\":");
        repository.Should().Contain(".Where((e, text) => e.Name.Contains(text), search).Take(take).ToListAsync(ct)");
    }

    [Fact]
    public void OnTheDocumentsEngine_TheEndpointReadsThroughTheCompiledRead()
    {
        var result = GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Entity + DocumentsBridge, "Pragmatic.Persistence.Documents.Generated",
            [.. References, GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>()]);
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        var endpoint = sources.Single(kv => kv.Key.Contains("SupplierNameAutocompleteEndpoint")).Value;
        endpoint.Should().NotContain("Microsoft.EntityFrameworkCore");
        endpoint.Should().Contain("AutocompleteAsync(\"Name\", search,");

        var repository = sources["Probe.Ac.Supplier.DocReadRepository.g.cs"];
        repository.Should().Contain("global::Pragmatic.Persistence.Repository.IAutocompleteReadRepository<global::Probe.Ac.Supplier>");
        repository.Should().Contain("case \"Name\":");
        repository.Should().Contain(".Where((e, text) => e.Name.Contains(text), search).Take(take).ToListAsync(ct)");
    }

    /// <summary>The control: with EF Core the only engine, the endpoint reads as it always did.</summary>
    [Fact]
    public void OnEfCore_TheEndpointReadsThroughQuery()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Entity,
            [.. References, GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()]);

        var endpoint = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Single(kv => kv.Key.Contains("SupplierNameAutocompleteEndpoint")).Value;
        endpoint.Should().Contain("repository.Query()");
        endpoint.Should().Contain("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
        endpoint.Should().NotContain("IAutocompleteReadRepository");
    }

    private static SourceGenRunResult Run(string source, string interceptors)
        => GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(source, interceptors, References);

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<AutocompleteAttribute>(),
        .. ByName("Pragmatic.Specification", "Pragmatic.Abstractions", "Pragmatic.Persistence.Expressions", "System.Linq.Queryable", "System.Linq.Expressions", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}
