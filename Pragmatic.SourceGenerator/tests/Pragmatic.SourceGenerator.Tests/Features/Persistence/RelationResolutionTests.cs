using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     One relationship resolves to one set of members, whichever end declared what.
/// </summary>
/// <remarks>
///     <para>
///         The parent's <c>OneToMany</c> and the child's <c>ManyToOne</c> both emit the child's
///         foreign key and reference navigation. While each derived its own answer they could
///         disagree, and the disagreement was settled by <em>member name</em>: naming things
///         differently produced two columns, naming them the same lost whichever option was
///         processed second.
///     </para>
///     <para>
///         Declaration order is therefore half of what these tests measure. Every case runs both
///         ways, because a fixture fixed to one order passes without the remedy — that is exactly how
///         an earlier attempt at this went green.
///     </para>
/// </remarks>
public class RelationResolutionTests
{
    private const string ParentType = "Shop.Entities.Invoice";
    private const string ChildType = "Shop.Entities.Fee";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheChildSaysItsColumnIsOptional_AndItIs(bool childFirst)
    {
        var fee = Resolve(childFirst, Child(required: false));

        TypeOfForeignKey(fee, "InvoiceId").Should().Be("System.Guid?",
            "the column lives on the child, so whether it may be null is the child's to say");
        NavigationTo(fee, "Invoice").IsRequired.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheChildNamesItsColumn_AndThereIsOnlyThatOne(bool childFirst)
    {
        var fee = Resolve(childFirst, Child(foreignKey: "InvoiceRef"));

        ForeignKeyNames(fee).Should().BeEquivalentTo(["InvoiceRef"],
            "one relationship has one foreign key — a second, derived from the parent's side, would "
            + "be an extra non-null column nobody writes");
        NavigationTo(fee, "Invoice").ForeignKeyProperty.Should().Be("InvoiceRef");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheParentNamesTheChildsNavigation_AndThereIsOnlyThatOne(bool childFirst)
    {
        var fee = Resolve(childFirst, Child(), parentInverse: "Owner");

        NavigationNames(fee).Should().BeEquivalentTo(["Owner"],
            "Inverse names the child's navigation; deriving a second one from the child's own "
            + "declaration would make one relationship into two");
        ForeignKeyNames(fee).Should().BeEquivalentTo(["OwnerId"]);
    }

    /// <summary>The delete behaviour still comes from the side that owns the relationship.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheDeleteBehaviourComesFromTheOwningSide(bool childFirst)
    {
        var fee = Resolve(childFirst, Child(), parentOnDelete: "Cascade");

        NavigationTo(fee, "Invoice").OnDelete.Should().Be("Cascade");
    }

    /// <summary>
    ///     The control that keeps all of it narrow: with no counterpart the child's declaration is the
    ///     only description there is, and every option on it stands.
    /// </summary>
    [Fact]
    public void WithNoCounterpart_TheChildsOwnDeclarationStands()
    {
        var graph = RelationGraphBuilder.BuildRelationGraph(
        [
            Entity(ChildType, "Fee", Child(required: false, foreignKey: "InvoiceRef", onDelete: "Restrict")),
            Entity(ParentType, "Invoice")
        ]);

        var fee = graph.Single(e => e.FullTypeName == ChildType);
        ForeignKeyNames(fee).Should().BeEquivalentTo(["InvoiceRef"]);
        TypeOfForeignKey(fee, "InvoiceRef").Should().Be("System.Guid?");
        NavigationTo(fee, "Invoice").OnDelete.Should().Be("Restrict");
    }

    private static EntityMetadataModel Resolve(
        bool childFirst,
        RelationAttributeModel childRelation,
        string? parentInverse = null,
        string parentOnDelete = "Cascade")
    {
        var child = Entity(ChildType, "Fee", childRelation);
        var parent = Entity(ParentType, "Invoice", new RelationAttributeModel
        {
            RelationType = "OneToMany",
            TargetTypeFullName = ChildType,
            TargetTypeName = "Fee",
            InverseProperty = parentInverse,
            OnDelete = parentOnDelete
        });

        var graph = RelationGraphBuilder.BuildRelationGraph(
            childFirst ? [child, parent] : [parent, child]);

        return graph.Single(e => e.FullTypeName == ChildType);
    }

    private static RelationAttributeModel Child(
        bool required = true,
        string? foreignKey = null,
        string onDelete = "Restrict") => new()
    {
        RelationType = "ManyToOne",
        TargetTypeFullName = ParentType,
        TargetTypeName = "Invoice",
        IsRequired = required,
        ForeignKeyProperty = foreignKey,
        OnDelete = onDelete
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

    private static ImmutableArray<string> ForeignKeyNames(EntityMetadataModel entity)
        => entity.GeneratedRelationProperties.AsImmutableArray()
            .Where(p => p.Kind == RelationPropertyKind.ForeignKey)
            .Select(p => p.Name)
            .ToImmutableArray();

    private static ImmutableArray<string> NavigationNames(EntityMetadataModel entity)
        => entity.GeneratedRelationProperties.AsImmutableArray()
            .Where(p => p.Kind == RelationPropertyKind.ReferenceNav)
            .Select(p => p.Name)
            .ToImmutableArray();

    private static string TypeOfForeignKey(EntityMetadataModel entity, string name)
        => entity.GeneratedRelationProperties.AsImmutableArray()
            .Single(p => p.Kind == RelationPropertyKind.ForeignKey && p.Name == name)
            .TypeName;

    private static NavigationMetadataModel NavigationTo(EntityMetadataModel entity, string parentTypeName)
        => entity.Navigations.AsImmutableArray()
            .Single(n => n.NavigationType == "ManyToOne" && n.TargetTypeName.EndsWith(parentTypeName));
}
