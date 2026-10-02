using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A relationship has one delete behaviour, whichever end declares it.
/// </summary>
/// <remarks>
///     <para>
///         The two attributes carry opposite defaults — <c>OneToMany</c> is <c>Cascade</c>,
///         <c>ManyToOne</c> is <c>Restrict</c> — so a relationship declared from both ends can
///         disagree with itself although nobody has written a value. If both configurations were
///         emitted, which one reached the model would depend on the order the graph walks the
///         entities in.
///     </para>
///     <para>
///         Declaration order is therefore part of what these tests measure: a fixture that declares
///         the parent first passes even with the remedy disabled, because deduplication keeps the
///         first contribution.
///     </para>
/// </remarks>
public class RelationDeleteBehaviourTests
{
    private const string Parent = "Shop.Entities.Invoice";
    private const string Child = "Shop.Entities.Fee";

    [Fact]
    public void BothSidesDeclared_ChildFirst_TheTwoEndsAgree()
        => BothEndsAgree(childFirst: true);

    [Fact]
    public void BothSidesDeclared_ParentFirst_TheTwoEndsAgree()
        => BothEndsAgree(childFirst: false);

    /// <summary>
    ///     The control that keeps the rule narrow: with no collection on the other side there is no
    ///     owning declaration to read, and the unidirectional <c>ManyToOne</c> keeps its own value.
    /// </summary>
    [Fact]
    public void AManyToOneWithNoCounterpart_KeepsItsOwnBehaviour()
    {
        var graph = RelationGraphBuilder.BuildRelationGraph(
        [
            EntityWith(Parent, "Invoice"),
            EntityWith(Child, "Fee", ManyToOneToParent("Restrict"))
        ]);

        DeleteBehaviourOf(graph, Child, "Invoice").Should().Be("Restrict",
            "nothing else describes this relationship, so the declaration is the only source there is");
    }

    /// <summary>The owning side's explicit value is the one that travels, not just its default.</summary>
    [Fact]
    public void AnExplicitBehaviourOnTheOwningSide_ReachesTheChild()
    {
        var graph = RelationGraphBuilder.BuildRelationGraph(
        [
            EntityWith(Child, "Fee", ManyToOneToParent("Restrict")),
            EntityWith(Parent, "Invoice", OneToManyToChild("SetNull"))
        ]);

        DeleteBehaviourOf(graph, Child, "Invoice").Should().Be("SetNull");
        DeleteBehaviourOf(graph, Parent, "Fees").Should().Be("SetNull");
    }

    private static void BothEndsAgree(bool childFirst)
    {
        var child = EntityWith(Child, "Fee", ManyToOneToParent("Restrict"));
        var parent = EntityWith(Parent, "Invoice", OneToManyToChild("Cascade"));

        var graph = RelationGraphBuilder.BuildRelationGraph(
            childFirst ? [child, parent] : [parent, child]);

        var onChild = DeleteBehaviourOf(graph, Child, "Invoice");
        var onParent = DeleteBehaviourOf(graph, Parent, "Fees");

        onChild.Should().Be(onParent,
            "one relationship has one delete behaviour, and both configurations are emitted");
        onChild.Should().Be("Cascade",
            "the side that owns the relationship is the one that describes it");
    }

    private static string DeleteBehaviourOf(
        ImmutableArray<EntityMetadataModel> graph,
        string entityFullName,
        string navigationName)
    {
        var entity = graph.Single(e => e.FullTypeName == entityFullName);
        var navigation = entity.Navigations.AsImmutableArray()
            .Single(n => n.Name == navigationName);
        return navigation.OnDelete;
    }

    private static RelationAttributeModel OneToManyToChild(string onDelete) => new()
    {
        RelationType = "OneToMany",
        TargetTypeFullName = Child,
        TargetTypeName = "Fee",
        OnDelete = onDelete
    };

    private static RelationAttributeModel ManyToOneToParent(string onDelete) => new()
    {
        RelationType = "ManyToOne",
        TargetTypeFullName = Parent,
        TargetTypeName = "Invoice",
        OnDelete = onDelete
    };

    private static EntityMetadataModel EntityWith(
        string fullTypeName,
        string typeName,
        params RelationAttributeModel[] relations) => new()
    {
        TypeName = typeName,
        FullTypeName = fullTypeName,
        Namespace = "Shop.Entities",
        IdType = "System.Guid",
        RelationAttributes = relations.ToEquatableArray(),
        UsesRelationAttributes = relations.Length > 0
    };
}
