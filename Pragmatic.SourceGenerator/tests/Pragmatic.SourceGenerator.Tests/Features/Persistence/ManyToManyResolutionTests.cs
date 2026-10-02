using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A many-to-many has one join, whichever end describes it.
/// </summary>
/// <remarks>
///     <para>
///         Neither end owns a symmetric relationship, so the join table and its keys may be written on
///         either. While each end read only its own declaration, two ends naming different tables
///         produced <b>two</b> join tables, and keys written on one end never reached the
///         configuration generated from the other.
///     </para>
///     <para>
///         "Mine, or else theirs" would not have fixed it: that hands each end a different answer.
///         The two declarations are ordered canonically first, which is why every case here runs both
///         ways round.
///     </para>
/// </remarks>
public class ManyToManyResolutionTests
{
    private const string PropertyType = "Shop.Entities.Property";
    private const string AmenityType = "Shop.Entities.Amenity";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoEndsNamingDifferentJoinTables_AgreeOnOne(bool propertyFirst)
    {
        var graph = Build(propertyFirst,
            Relation(AmenityType, "Amenity", joinTable: "PropertyAmenities"),
            Relation(PropertyType, "Property", joinTable: "AmenityProperties"));

        JoinTables(graph).Distinct().Should().HaveCount(1,
            "one relationship has one join table, and two configurations naming different ones "
            + "create two");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeysNamedOnOneEnd_ReachTheOther(bool propertyFirst)
    {
        var graph = Build(propertyFirst,
            Relation(AmenityType, "Amenity", left: "PropertyRef", right: "AmenityRef"),
            Relation(PropertyType, "Property"));

        var onProperty = Navigation(graph, PropertyType);
        var onAmenity = Navigation(graph, AmenityType);

        onProperty.JoinLeftKey.Should().Be("PropertyRef");
        onProperty.JoinRightKey.Should().Be("AmenityRef");

        onAmenity.JoinLeftKey.Should().Be("AmenityRef",
            "seen from the other end the two sides of the join are the other way round");
        onAmenity.JoinRightKey.Should().Be("PropertyRef");
    }

    /// <summary>
    ///     The collection the generator creates on the other end has to be named to EF, or it maps it
    ///     by convention as a relationship of its own.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheDeclaringEndNamesTheCollectionItGenerates(bool propertyFirst)
    {
        var graph = Build(propertyFirst, Relation(AmenityType, "Amenity"), null);

        Navigation(graph, PropertyType).InverseProperty.Should().Be("Properties",
            "that collection is generated on Amenity whether or not Amenity declares anything");
    }

    /// <summary>
    ///     The inverse collection appears because this relationship was declared — not because the
    ///     other entity happens to declare some unrelated one.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATargetThatDeclaresNothing_StillGetsItsCollection(bool propertyFirst)
    {
        var graph = Build(propertyFirst, Relation(AmenityType, "Amenity"), null);

        Collections(graph, AmenityType).Should().BeEquivalentTo(["Properties"],
            "whether this relationship produced its other end used to depend on whether Amenity "
            + "declared some other, unrelated relation");
    }

    private static ImmutableArray<EntityMetadataModel> Build(
        bool propertyFirst, RelationAttributeModel propertyRelation, RelationAttributeModel? amenityRelation)
    {
        var property = Entity(PropertyType, "Property", propertyRelation);
        var amenity = amenityRelation is null
            ? Entity(AmenityType, "Amenity")
            : Entity(AmenityType, "Amenity", amenityRelation);

        return RelationGraphBuilder.BuildRelationGraph(
            propertyFirst ? [property, amenity] : [amenity, property]);
    }

    private static RelationAttributeModel Relation(
        string targetFull, string targetName,
        string? joinTable = null, string? left = null, string? right = null) => new()
    {
        RelationType = "ManyToMany",
        TargetTypeFullName = targetFull,
        TargetTypeName = targetName,
        JoinTable = joinTable,
        JoinLeftKey = left,
        JoinRightKey = right
    };

    private static EntityMetadataModel Entity(
        string fullTypeName, string typeName, params RelationAttributeModel[] relations) => new()
    {
        TypeName = typeName,
        FullTypeName = fullTypeName,
        Namespace = "Shop.Entities",
        IdType = "System.Guid",
        RelationAttributes = relations.ToEquatableArray(),
        UsesRelationAttributes = relations.Length > 0
    };

    private static NavigationMetadataModel Navigation(
        ImmutableArray<EntityMetadataModel> graph, string fullTypeName)
        => graph.Single(e => e.FullTypeName == fullTypeName)
            .Navigations.AsImmutableArray()
            .Single(n => n.NavigationType == "ManyToMany");

    private static ImmutableArray<string?> JoinTables(ImmutableArray<EntityMetadataModel> graph)
        => graph.SelectMany(e => e.Navigations.AsImmutableArray())
            .Where(n => n.NavigationType == "ManyToMany")
            .Select(n => n.JoinTable)
            .ToImmutableArray();

    private static ImmutableArray<string> Collections(
        ImmutableArray<EntityMetadataModel> graph, string fullTypeName)
        => graph.Single(e => e.FullTypeName == fullTypeName)
            .GeneratedRelationProperties.AsImmutableArray()
            .Where(p => p.Kind == RelationPropertyKind.Collection)
            .Select(p => p.Name)
            .ToImmutableArray();
}
