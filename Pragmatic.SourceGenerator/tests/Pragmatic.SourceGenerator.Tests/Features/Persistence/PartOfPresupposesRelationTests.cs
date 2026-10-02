using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[PartOf&lt;T&gt;]</c> is a role on a relation: the relation has to be there, be the one meant,
///     and — for a part with no life of its own — cascade.
/// </summary>
/// <remarks>
///     <para>
///         Structure and lifecycle are two axes, and until here they never met: the relation graph did
///         not read <c>[PartOf]</c>, so a part with no relation to its whole produced nothing — no
///         column, no navigation, no cascade — and nobody said so.
///     </para>
///     <para>
///         The cascade rule holds for an exclusive part only. <c>Exclusive = false</c> is «the parent
///         may write it» without «it has no life of its own», and the conformance delivery address —
///         written through its order, addressable alone, detachable, key on the order — is that shape
///         on purpose.
///     </para>
/// </remarks>
public class PartOfPresupposesRelationTests
{
    private static readonly string[] Ids = ["PRAG0631", "PRAG0632", "PRAG0633", "PRAG0634"];

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Sales;

            [Boundary]
            public partial class SalesBoundary;

            {{body}}
            """, References);

    private static void Silent(SourceGenRunResult result)
    {
        foreach (var id in Ids)
            GeneratorTestHelper.HasDiagnostic(result, id).Should().BeFalse(id);
    }

    [Fact]
    public void PartOfWithNoRelationToTheParent_IsReported()
    {
        var result = Run("""
            [Entity]
            public partial class Order : IEntity { }

            [Entity]
            [PartOf<Order>]
            public partial class OrderLine : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0631").Should().BeTrue(
            "the ownership has no edge to live on: no column, no navigation, no cascade");
    }

    /// <summary>Dropping the exclusivity does not drop the need for an edge.</summary>
    [Fact]
    public void NonExclusivePartWithNoRelationEitherWay_IsStillReported()
    {
        var result = Run("""
            [Entity]
            public partial class Order : IEntity { }

            [Entity]
            [PartOf<Order>(Exclusive = false)]
            public partial class DeliveryAddress : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0631").Should().BeTrue();
    }

    [Fact]
    public void TwoRelationsToTheParent_WithoutVia_IsReported()
    {
        var result = Run("""
            [Entity]
            public partial class Order : IEntity { }

            [Entity]
            [Relation.ManyToOne<Order>.WithNavigation("Order", OnDelete = DeleteBehavior.Cascade)]
            [Relation.ManyToOne<Order>.WithNavigation("ReturnedTo", Required = false)]
            [PartOf<Order>]
            public partial class OrderLine : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0632").Should().BeTrue(
            "a part is written along one edge, and with two edges to the same parent only a name says which");
    }

    [Fact]
    public void ViaNamingNoRelationToTheParent_IsReported()
    {
        var result = Run("""
            [Entity]
            public partial class Order : IEntity { }

            [Entity]
            [Relation.ManyToOne<Order>.WithNavigation("Order", OnDelete = DeleteBehavior.Cascade)]
            [PartOf<Order>(Via = "Owner")]
            public partial class OrderLine : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0633").Should().BeTrue();
    }

    [Fact]
    public void AnExclusivePartWhoseEdgeDoesNotCascade_IsReported()
    {
        var result = Run("""
            [Entity]
            [Relation.OneToMany<OrderLine>.WithNavigation("Lines", OnDelete = DeleteBehavior.Restrict)]
            public partial class Order : IEntity { }

            [Entity]
            [Relation.ManyToOne<Order>]
            [PartOf<Order>]
            public partial class OrderLine : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0634").Should().BeTrue(
            "a part with no life of its own goes when its whole goes — Restrict on the owning side "
            + "contradicts the declaration");
    }

    /// <summary>The ordinary shape: the parent's collection cascades by default, the child declares its end.</summary>
    [Fact]
    public void TheOrdinaryShape_IsSilent()
    {
        Silent(Run("""
            [Entity]
            [Relation.OneToMany<OrderLine>.WithNavigation("Lines")]
            public partial class Order : IEntity { }

            [Entity]
            [Relation.ManyToOne<Order>]
            [PartOf<Order>]
            public partial class OrderLine : IEntity { }
            """));
    }

    /// <summary>Two edges to the parent, the ownership named: nothing to report.</summary>
    [Fact]
    public void TwoRelations_WithViaOnTheCascadingOne_IsSilent()
    {
        Silent(Run("""
            [Entity]
            [Relation.OneToMany<OrderLine>.WithNavigation("Lines", Inverse = "Order")]
            [Relation.OneToMany<OrderLine>.WithNavigation("Returns", Inverse = "ReturnedTo", OnDelete = DeleteBehavior.Restrict)]
            public partial class Order : IEntity { }

            [Entity]
            [Relation.ManyToOne<Order>.WithNavigation("Order")]
            [Relation.ManyToOne<Order>.WithNavigation("ReturnedTo", Required = false)]
            [PartOf<Order>(Via = "Order")]
            public partial class OrderLine : IEntity { }
            """));
    }

    /// <summary>
    ///     The conformance delivery address: written through its order, addressable alone, and the key
    ///     on the order. A non-exclusive part leans on the parent's edge and needs no cascade.
    /// </summary>
    [Fact]
    public void ANonExclusivePartReferencedByItsParent_IsSilent()
    {
        Silent(Run("""
            [Entity]
            [Relation.ManyToOne<DeliveryAddress>.WithNavigation("DeliveryAddress", Required = false)]
            public partial class Order : IEntity { }

            [Entity]
            [PartOf<Order>(Exclusive = false)]
            public partial class DeliveryAddress : IEntity { }
            """));
    }

    /// <summary>The same shape declared exclusive cannot hold: a key on the parent never cascades to the part.</summary>
    [Fact]
    public void AnExclusivePartReferencedByItsParent_IsReported()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<DeliveryAddress>.WithNavigation("DeliveryAddress", Required = false)]
            public partial class Order : IEntity { }

            [Entity]
            [PartOf<Order>]
            public partial class DeliveryAddress : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0634").Should().BeTrue(
            "deleting the order cannot take the address with it when the key sits on the order");
    }

    /// <summary>
    ///     The control that keeps the rule on its own axis: a child with the relation but without
    ///     <c>[PartOf]</c> is a plain dependent, and none of this applies.
    /// </summary>
    [Fact]
    public void ARelationWithoutPartOf_IsSilent()
    {
        Silent(Run("""
            [Entity]
            [Relation.OneToMany<RoomType>.WithNavigation("RoomTypes", OnDelete = DeleteBehavior.Restrict)]
            public partial class Property : IEntity { }

            [Entity]
            [Relation.ManyToOne<Property>]
            public partial class RoomType : IEntity { }
            """));
    }
}
