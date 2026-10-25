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

    private const string Entities = """
        using Pragmatic.Persistence.Entity;

        namespace Probe.Loads
        {
            [Entity] public partial class Bed : IEntity { public string Label { get; set; } = ""; }

            [Entity] public partial class Room : IEntity
            {
                public string Code { get; set; } = "";
                public System.Collections.Generic.List<Bed> Beds { get; set; } = [];
            }
        }
        """;

    private const string Loads = Entities + """

        namespace Probe.Loads
        {
            [Pragmatic.Actions.Attributes.LoadEntity<Room>(nameof(Id), Include = nameof(Room.Beds))]
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

    private const string EagerMutation = """

        namespace Probe.Loads
        {
            [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Update)]
            [Pragmatic.Actions.Mutation.EagerLoad(nameof(Room.Beds))]
            public partial class RenameRoomMutation : Pragmatic.Actions.Mutation.Mutation<Room>
            {
                public required System.Guid Id { get; init; }

                public string? Code { get; init; }
            }
        }
        """;

    [Fact]
    public void OnTheDocumentsEngine_AMutationThatLoadsANavigation_IsPrag3112()
    {
        var result = Run(Entities + EagerMutation + DocumentsBridge);

        var refused = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3112").ToList();
        refused.Count.Should().Be(1);
        refused[0].GetMessage().Should().Contain("RenameRoomMutation loads the navigations Beds of Room");
        refused[0].Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public void OnTheSqlEngine_AMutationThatLoadsANavigation_IsAReadTheRepositoryCompiles()
    {
        var result = Run(Entities + EagerMutation + SqlBridge);

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        sources["Probe.Loads.Room.SqlReadRepository.g.cs"].Should().Contain("case \"Beds\":");
        sources.Values.Should().Contain(s => s.Contains("class RenameRoomMutation", StringComparison.Ordinal)
                                             && s.Contains(".GetByIdIncludingAsync(mutation.Id, \"Beds\", ct)", StringComparison.Ordinal));
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3037").Should().BeEmpty("the mutation writes no children");
    }

    private const string ChildWrites = """
        using Pragmatic.Persistence.Entity;

        namespace Probe.Writes
        {
            [Entity] public partial class Room : IEntity
            {
                public string Code { get; set; } = "";
                public System.Collections.Generic.List<Bed> Beds { get; set; } = [];
            }

            [Entity]
            [Relation.ManyToOne<Room>]
            [PartOf<Room>]
            public partial class Bed : IEntity { public string Label { get; set; } = ""; }

            [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Update)]
            public partial class WriteBedMutation : Pragmatic.Actions.Mutation.Mutation<Bed>
            {
                public System.Guid Id { get; init; }

                public string Label { get; init; } = "";
            }

            [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Update)]
            public partial class SetBedsMutation : Pragmatic.Actions.Mutation.Mutation<Room>
            {
                public required System.Guid Id { get; init; }

                public required System.Collections.Generic.List<WriteBedMutation> Beds { get; init; }
            }
        }
        """;

    /// <summary>A merge into a collection: the load reads the collection it merges into, compiled; the unit of work saves the graph.</summary>
    [Fact]
    public void OnTheSqlEngine_AMutationThatMergesACollection_LoadsItCompiled()
    {
        var result = Run(ChildWrites + SqlBridge);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3037").Should().BeEmpty("a collection is part of the graph the unit of work saves");
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        sources["Probe.Writes.Room.SqlReadRepository.g.cs"].Should().Contain("case \"Beds\":");
        sources.Values.Should().Contain(s => s.Contains("class SetBedsMutation", StringComparison.Ordinal)
                                             && s.Contains(".GetByIdIncludingAsync(mutation.Id, \"Beds\", ct)", StringComparison.Ordinal));
    }

    private const string DeepWrites = """
        using Pragmatic.Persistence.Entity;

        namespace Probe.Deep
        {
            [Entity] public partial class Room : IEntity
            {
                public System.Collections.Generic.List<Bed> Beds { get; set; } = [];
            }

            [Entity]
            [Relation.ManyToOne<Room>]
            [PartOf<Room>]
            public partial class Bed : IEntity
            {
                public System.Collections.Generic.List<Pillow> Pillows { get; set; } = [];
            }

            [Entity]
            [Relation.ManyToOne<Bed>]
            [PartOf<Bed>]
            public partial class Pillow : IEntity { public string Fill { get; set; } = ""; }

            [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Update)]
            public partial class WritePillowMutation : Pragmatic.Actions.Mutation.Mutation<Pillow>
            {
                public System.Guid Id { get; init; }

                public string Fill { get; init; } = "";
            }

            [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Update)]
            public partial class WriteBedMutation : Pragmatic.Actions.Mutation.Mutation<Bed>
            {
                public System.Guid Id { get; init; }

                public System.Collections.Generic.List<WritePillowMutation> Pillows { get; init; } = [];
            }

            [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Update)]
            public partial class SetBedsMutation : Pragmatic.Actions.Mutation.Mutation<Room>
            {
                public required System.Guid Id { get; init; }

                public required System.Collections.Generic.List<WriteBedMutation> Beds { get; init; }
            }
        }
        """;

    /// <summary>
    ///     A child mutation that merges children of its own: the load reads them too, the path the child's <c>WrittenNavigations</c> lists at run
    ///     time, known at build.
    /// </summary>
    [Fact]
    public void OnTheSqlEngine_AChildMutationsOwnMerge_IsLoadedToo()
    {
        var result = Run(DeepWrites + SqlBridge);

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Values.Should().Contain(s => s.Contains("class SetBedsMutation", StringComparison.Ordinal)
            && s.Contains(".GetByIdIncludingAsync(mutation.Id, \"Beds,Beds.Pillows\", ct)", StringComparison.Ordinal));
    }

    private const string CascadingDelete = """
        using Pragmatic.Persistence.Entity;

        namespace Probe.Cascades
        {
            [Entity]
            [SoftDelete(Cascade = true)]
            [Relation.OneToMany<Bed>.WithNavigation("Beds")]
            public partial class Room : IEntity { public string Code { get; set; } = ""; }

            [Entity]
            [SoftDelete]
            [Relation.ManyToOne<Room>]
            public partial class Bed : IEntity { public string Label { get; set; } = ""; }

            [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Delete)]
            public partial class CloseRoomMutation : Pragmatic.Actions.Mutation.Mutation<Room>
            {
                public required System.Guid Id { get; init; }
            }
        }
        """;

    /// <summary>A delete whose soft delete cascades into a collection: the load reads the children it hides, compiled.</summary>
    [Fact]
    public void OnTheSqlEngine_ADeleteThatCascadesIntoACollection_LoadsItCompiled()
    {
        var result = Run(CascadingDelete + SqlBridge);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3037").Should().BeEmpty("a collection is part of the graph the unit of work saves");
        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Values.Should().Contain(s => s.Contains("class CloseRoomMutation", StringComparison.Ordinal)
            && s.Contains(".GetByIdIncludingAsync(mutation.Id, \"Beds\", ct)", StringComparison.Ordinal));
    }

    private const string CascadingReference = """
        using Pragmatic.Persistence.Entity;

        namespace Probe.References
        {
            [Entity]
            [SoftDelete(Cascade = true)]
            [Relation.OneToOne<Plaque>.WithNavigation("Plaque")]
            public partial class Room : IEntity { public string Code { get; set; } = ""; }

            [Entity]
            [SoftDelete]
            public partial class Plaque : IEntity { public string Text { get; set; } = ""; }

            [Pragmatic.Actions.Mutation.Mutation(Mode = Pragmatic.Actions.Mutation.MutationMode.Delete)]
            public partial class CloseRoomMutation : Pragmatic.Actions.Mutation.Mutation<Room>
            {
                public required System.Guid Id { get; init; }
            }
        }
        """;

    /// <summary>A cascade into a to-one navigation: outside the graph the unit of work saves, so refused.</summary>
    [Fact]
    public void OnTheSqlEngine_ADeleteThatCascadesIntoAReference_IsPrag3037()
    {
        var result = Run(CascadingReference + SqlBridge);

        var refused = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3037").ToList();
        refused.Count.Should().Be(1);
        refused[0].GetMessage().Should().Contain("CloseRoomMutation writes the child reference of Room (Plaque)");
        refused[0].Severity.Should().Be(DiagnosticSeverity.Error);
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
