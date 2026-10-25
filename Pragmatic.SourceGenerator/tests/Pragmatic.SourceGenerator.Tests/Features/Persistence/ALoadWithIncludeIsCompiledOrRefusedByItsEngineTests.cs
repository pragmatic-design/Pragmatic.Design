using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>[LoadEntity(Include = …)]</c> on an engine that compiles its reads: the Sql engine compiles a read by key per set of paths in the
///     entity's repository, and refuses the forms it does not compile (PRAG3036); the documents engine refuses every one (PRAG3111), since a
///     document keeps a relation as its key.
/// </summary>
/// <remarks>
///     The bridges are stubs of the types the generator recognises them by, as in <see cref="TheDocumentsEngineServesPragmaticsEntitiesTests" />:
///     these tests read the diagnostics and the generated text. The Sql bridge suite (<c>Pragmatic.Persistence.Sql.Tests</c>) runs the
///     compiled read through a generated invoker on every dialect.
/// </remarks>
public class ALoadWithIncludeIsCompiledOrRefusedByItsEngineTests
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

    private const string Loads = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Loads
        {
            [Entity] public partial class Bed : IEntity { public string Label { get; set; } = ""; }

            [Entity] public partial class Room : IEntity
            {
                public string Code { get; set; } = "";
                public System.Collections.Generic.List<Bed> Beds { get; set; } = [];
            }

            [LoadEntity<Room>(nameof(Id), Include = nameof(Room.Beds))]
            public partial class ReadBeds { public System.Guid Id { get; init; } }
        }
        """;

    private const string ByLogicKey = """

        namespace Probe.Loads
        {
            [Pragmatic.Actions.Attributes.LoadEntity<Room>(nameof(Code), By = nameof(Room.Code), Include = nameof(Room.Beds))]
            public partial class ReadBedsByCode { public string Code { get; init; } = ""; }
        }
        """;

    [Fact]
    public void OnTheSqlEngine_ALoadByKey_IsAReadTheRepositoryCompiles()
    {
        var result = Run(Loads + SqlBridge);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3036").Should().BeEmpty("a load by key of an entity of this assembly is compiled");
        var repository = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)["Probe.Loads.Room.SqlReadRepository.g.cs"];
        repository.Should().Contain("global::Pragmatic.Persistence.Repository.IIncludingReadRepository<global::Probe.Loads.Room>");
        repository.Should().Contain("case \"Beds\":");
        repository.Should().Contain(".Include(e => e.Beds).FirstOrDefaultAsync((e, key) => e.PersistenceId == key, id, ct)");
    }

    [Fact]
    public void OnTheSqlEngine_ALoadByTheLogicKey_IsPrag3036()
    {
        var result = Run(Loads + ByLogicKey + SqlBridge);

        var refused = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3036").ToList();
        refused.Count.Should().Be(1, "the load by key is compiled, the one by the logic key is not");
        refused[0].GetMessage().Should().Contain("ReadBedsByCode");
        refused[0].GetMessage().Should().Contain("it loads by the logic key (By)");
        refused[0].Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public void OnTheDocumentsEngine_ALoadWithInclude_IsPrag3111()
    {
        var result = Run(Loads + DocumentsBridge);

        var refused = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3111").ToList();
        refused.Count.Should().Be(1);
        refused[0].GetMessage().Should().Contain("[LoadEntity<Room>] on ReadBeds loads with Include");
        refused[0].Severity.Should().Be(DiagnosticSeverity.Error);
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3036").Should().BeEmpty("the Sql engine is not referenced");
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(source, "Pragmatic.Persistence.Documents.Generated", References);

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>(),
        .. ByName("Pragmatic.Specification", "Pragmatic.Abstractions", "Pragmatic.Persistence.Expressions", "System.Linq.Queryable", "System.Linq.Expressions", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}
