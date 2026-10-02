using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A one-to-one has one foreign key, on the end that is not the principal.
/// </summary>
/// <remarks>
///     <para>
///         Both ends declare the same attribute, so nothing but <c>IsPrincipal</c> tells them apart.
///         While each end decided its own role, a relationship nobody claimed produced <b>two</b>
///         foreign keys — one per table — and one both claimed produced <b>none</b>. Neither said so.
///     </para>
///     <para>
///         Order matters here too: the roles must come out the same whichever end the graph reaches
///         first.
///     </para>
/// </remarks>
public class OneToOneResolutionTests
{
    private const string GuestType = "Shop.Entities.Guest";
    private const string PreferencesType = "Shop.Entities.GuestPreferences";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnePrincipal_OnlyTheDependentCarriesTheKey(bool guestFirst)
    {
        var graph = Build(guestFirst, guestIsPrincipal: true, preferencesIsPrincipal: false);

        ForeignKeys(graph, GuestType).Should().BeEmpty("the principal is the end without the key");
        ForeignKeys(graph, PreferencesType).Should().BeEquivalentTo(["GuestId"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NobodyIsPrincipal_StillOnlyOneKey(bool guestFirst)
    {
        var graph = Build(guestFirst, guestIsPrincipal: false, preferencesIsPrincipal: false);

        TotalForeignKeys(graph).Should().Be(1,
            "both ends behaving as the dependent gave the relationship a key on each table");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EverybodyIsPrincipal_ThereIsStillAKey(bool guestFirst)
    {
        var graph = Build(guestFirst, guestIsPrincipal: true, preferencesIsPrincipal: true);

        TotalForeignKeys(graph).Should().Be(1,
            "both ends behaving as the principal left the relationship with no key at all");
    }

    /// <summary>
    ///     The generated member on the principal has to be named to EF, or it maps it by convention
    ///     as a second relationship with a shadow key of its own.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheDependentNamesTheNavigationItGeneratesOnThePrincipal(bool guestFirst)
    {
        var graph = Build(guestFirst, guestIsPrincipal: true, preferencesIsPrincipal: false);

        var dependent = graph.Single(e => e.FullTypeName == PreferencesType);
        var navigation = dependent.Navigations.AsImmutableArray()
            .Single(n => n.NavigationType == "OneToOne" && !n.IsPrincipal);

        navigation.InverseProperty.Should().Be("GuestPreferences",
            "that member is generated on Guest whether or not this end named it");
    }

    /// <summary>A foreign key written on the principal is not the principal's to name.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheDependentNamesTheKey(bool guestFirst)
    {
        var graph = RelationGraphBuilder.BuildRelationGraph(Order(guestFirst,
            Entity(GuestType, "Guest", Relation(PreferencesType, "GuestPreferences", true)),
            Entity(PreferencesType, "GuestPreferences",
                Relation(GuestType, "Guest", false, foreignKey: "OwningGuest"))));

        ForeignKeys(graph, PreferencesType).Should().BeEquivalentTo(["OwningGuest"]);
    }

    private static ImmutableArray<EntityMetadataModel> Build(
        bool guestFirst, bool guestIsPrincipal, bool preferencesIsPrincipal)
        => RelationGraphBuilder.BuildRelationGraph(Order(guestFirst,
            Entity(GuestType, "Guest", Relation(PreferencesType, "GuestPreferences", guestIsPrincipal)),
            Entity(PreferencesType, "GuestPreferences",
                Relation(GuestType, "Guest", preferencesIsPrincipal))));

    private static ImmutableArray<EntityMetadataModel> Order(
        bool firstFirst, EntityMetadataModel first, EntityMetadataModel second)
        => firstFirst ? [first, second] : [second, first];

    private static RelationAttributeModel Relation(
        string targetFull, string targetName, bool isPrincipal, string? foreignKey = null) => new()
    {
        RelationType = "OneToOne",
        TargetTypeFullName = targetFull,
        TargetTypeName = targetName,
        IsPrincipal = isPrincipal,
        ForeignKeyProperty = foreignKey,
        OnDelete = "Restrict"
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

    private static ImmutableArray<string> ForeignKeys(
        ImmutableArray<EntityMetadataModel> graph, string fullTypeName)
        => graph.Single(e => e.FullTypeName == fullTypeName)
            .GeneratedRelationProperties.AsImmutableArray()
            .Where(p => p.Kind == RelationPropertyKind.ForeignKey)
            .Select(p => p.Name)
            .ToImmutableArray();

    private static int TotalForeignKeys(ImmutableArray<EntityMetadataModel> graph)
        => graph.Sum(e => e.GeneratedRelationProperties.AsImmutableArray()
            .Count(p => p.Kind == RelationPropertyKind.ForeignKey));
}
