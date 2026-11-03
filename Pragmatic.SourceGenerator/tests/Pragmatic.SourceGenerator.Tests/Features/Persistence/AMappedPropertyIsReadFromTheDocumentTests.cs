using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A declared query's result with <c>[MapProperty]</c> on the documents engine (#476): a path of the document's own fields, nested in an owned
///     one too, is projected; a path through a navigation stays PRAG3108, naming the navigation, since a document keeps a relation as its key.
///     Before, every <c>[MapProperty]</c> refused the query, as it refused the Showcase's <c>SearchRoomTypesQuery</c>.
/// </summary>
/// <remarks>The bridge is declared by the types the generator recognises it by, as in <see cref="TheDocumentsEngineServesPragmaticsEntitiesTests" />.</remarks>
public class AMappedPropertyIsReadFromTheDocumentTests
{
    private const string DocumentsBridge = """
        namespace Pragmatic.Persistence.Documents
        {
            public static class PragmaticDocFilters { }
            public sealed class DocCompiledSpecification<T> { }
            public sealed class DocUnitOfWork { }

            public static class DocSpecifications
            {
                public static global::Pragmatic.Persistence.Documents.Planning.DocPredicate<T> ToPredicate<T>(global::Pragmatic.Specification.ISpecification<T> specification) => throw null!;
            }
        }
        """;

    // The attributes by the names the generator reads them by.
    private const string Rooms = """
        namespace Probe.Mapped
        {
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class MapPropertyAttribute(string path) : System.Attribute { public string Path { get; } = path; }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class OwnedAttribute : System.Attribute { }

            [Owned]
            public sealed class Placement { public string Floor { get; set; } = ""; public int Wing { get; set; } }

            [Pragmatic.Persistence.Entity.Entity]
            public partial class Building : Pragmatic.Persistence.Entity.IEntity { public string Name { get; set; } = ""; }

            [Pragmatic.Persistence.Entity.Entity]
            public partial class Room : Pragmatic.Persistence.Entity.IEntity
            {
                public string Code { get; set; } = "";
                public Placement Placement { get; set; } = new();
                public System.Guid BuildingId { get; set; }
                public Building? Building { get; set; }
            }

            public sealed class RoomCard
            {
                [MapProperty("Code")] public string Label { get; init; } = "";
                [MapProperty("Placement.Floor")] public string Floor { get; init; } = "";
            }

            public sealed class RoomWithBuilding
            {
                public string Code { get; init; } = "";
                [MapProperty("Building.Name")] public string BuildingName { get; init; } = "";
            }

            [Pragmatic.Persistence.Query.Attributes.Query<Room, RoomCard>]
            public partial class RoomCardsQuery { public string? Code { get; init; } }

            [Pragmatic.Persistence.Query.Attributes.Query<Room, RoomWithBuilding>]
            public partial class RoomsWithBuildingQuery { public string? Code { get; init; } }
        }
        """;

    [Fact]
    public void APathOfTheDocumentsOwnFields_IsProjected()
    {
        var result = Run(Rooms + DocumentsBridge);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3108").Should().NotContain(d => d.GetMessage().Contains("RoomCardsQuery", StringComparison.Ordinal),
            "a renamed field and a field of an owned one are the document's own");
        var generated = string.Concat(GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Values);
        generated.Should().Contain("Label = e.Code", "the renamed field is read from the field it names");
        generated.Should().Contain("Floor = e.Placement.Floor", "the owned field's member is read through it");
    }

    [Fact]
    public void APathThroughANavigation_IsPrag3108_NamingIt()
    {
        var result = Run(Rooms + DocumentsBridge);

        var refused = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3108").Where(d => d.GetMessage().Contains("RoomsWithBuildingQuery", StringComparison.Ordinal)).ToList();
        refused.Count.Should().Be(1, "the navigation's member is not in the document");
        refused[0].GetMessage().Should().Contain("through the navigation Building", "the diagnostic names the navigation, not only the property");
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(source, "Pragmatic.Persistence.Documents.Generated", References);

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.Mongo.MongoClient>(),
        .. ByName("Pragmatic.Specification", "Pragmatic.Abstractions", "Pragmatic.Persistence.Expressions", "System.Linq.Queryable", "System.Linq.Expressions", "System.Text.Json", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}
