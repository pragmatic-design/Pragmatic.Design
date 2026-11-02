using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resource;

/// <summary>
///     The read query a <c>[Resource]</c> scaffolds is compiled by the engine that persists its entity, as a hand-written <c>[Query]</c> is: the
///     engine gives it its <c>IQuery</c> shape and a case of its repository's <c>RunAsync</c>.
/// </summary>
/// <remarks>
///     An engine compiles a declared query it sees, and the scaffolded one is this generator's output, which the compilation the engine is
///     handed does not have. Without it in the engine's sources the query had neither, and its invoker's <c>RunAsync</c> call did not compile
///     (CS0411). The Sql engine's bridge is declared by the types the generator recognises it by, as in
///     <c>ALoadWithIncludeIsCompiledOrRefusedByItsEngineTests</c>.
/// </remarks>
public class AScaffoldedQueryIsCompiledByItsEngineTests
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

    private const string Source = """
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Showcase.Booking
        {
            [Boundary]
            public partial class BookingBoundary;
        }

        namespace Showcase.Booking.Entities
        {
            [Entity]
            [Resource("templates", Capabilities = ResourceCapabilities.Read)]
            public partial class Template : IEntity
            {
                public string Name { get; private set; } = "";
            }
        }
        """;

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.ResourceAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Actions.Mutation.Mutation<>)),
        GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
        .. ByName("Pragmatic.Specification", "Pragmatic.Abstractions", "Pragmatic.Persistence.Expressions", "System.Linq.Queryable", "System.Linq.Expressions", "System.ComponentModel.Annotations")
    ];

    [Fact]
    public void OnTheSqlEngine_TheScaffoldedReadIsADeclaredQueryOfTheRepository()
    {
        var result = GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Source + SqlBridge, "Pragmatic.Persistence.Sql.Generated", References);
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        sources.Values.Should().Contain(s => s.Contains("partial class ResourceReadTemplateQuery : global::Pragmatic.Persistence.Query.Interfaces.IQuery<"),
            "the engine gives the scaffolded query its shape, as it gives a hand-written [Query]");
        sources["Showcase.Booking.Entities.Template.SqlReadRepository.g.cs"].Should().Contain("case global::Showcase.Booking.Entities.ResourceReadTemplateQuery",
            "and a case of RunAsync, which the query's invoker calls");
    }

    private const string DocumentsBridge = """
        namespace Pragmatic.Persistence.Documents
        {
            public static class PragmaticDocFilters { }
            public sealed class DocCompiledSpecification<T> { }
            public sealed class DocUnitOfWork { }
        }
        """;

    [Fact]
    public void OnTheDocumentsEngine_TheScaffoldedReadIsADeclaredQueryOfTheRepository()
    {
        var result = GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Source + DocumentsBridge, "Pragmatic.Persistence.Documents.Generated",
            [.. References, GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>()]);
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        sources["Showcase.Booking.Entities.Template.DocReadRepository.g.cs"].Should().Contain("case global::Showcase.Booking.Entities.ResourceReadTemplateQuery",
            "the documents engine compiles the scaffolded read, as the Sql engine does");
    }

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}
