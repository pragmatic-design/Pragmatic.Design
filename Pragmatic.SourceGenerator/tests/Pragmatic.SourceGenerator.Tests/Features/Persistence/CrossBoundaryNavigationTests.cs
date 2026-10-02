using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A relation across a boundary always generates the key, and the navigation only when the
///     boundary reads the other side with <c>[ReadAccess&lt;T&gt;]</c> — read-only, and includable.
/// </summary>
/// <remarks>
///     A navigation across a boundary is generated, never written by hand — the hand-written form is
///     what <c>PRAG0619</c> forbids. The three cases share one
///     fixture and differ by the attribute alone, so the attribute is the only thing measured.
/// </remarks>
public class CrossBoundaryNavigationTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(MapFromAttribute<>)),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string salesBoundaryAttributes)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Mapping.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Sales;

            [Boundary]
            [Owns<CatalogItem>]
            public partial class CatalogBoundary;

            [Boundary]
            [Owns<Order>]
            [Owns<OrderLine>]
            {{salesBoundaryAttributes}}
            public partial class SalesBoundary;

            [Entity]
            public partial class CatalogItem : IEntity { public string Name { get; private set; } = ""; }

            [Entity]
            [Relation.ManyToOne<CatalogItem>.WithNavigation("Item")]
            [Relation.OneToMany<OrderLine>.WithNavigation("Lines")]
            public partial class Order : IEntity { public string Reference { get; private set; } = ""; }

            [Entity]
            [Relation.ManyToOne<Order>.WithNavigation("Order")]
            public partial class OrderLine : IEntity { public int Quantity { get; private set; } }

            [MapFrom<Order>]
            public partial class OrderDto { public string Reference { get; set; } = ""; }
            """, References);

    private static string SourceEndingWith(SourceGenRunResult result, string suffix)
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var hit = sources.FirstOrDefault(kv => kv.Key.Contains(suffix));
        hit.Value.Should().NotBeNull($"the generator emits {suffix} for the entity");
        return hit.Value!;
    }

    [Fact]
    public void WithoutReadAccess_OnlyTheKeyCrosses()
    {
        var result = Run("");

        var relations = SourceEndingWith(result, "Order.Relations");
        relations.Should().Contain("ItemId",
            "the key crosses the boundary: the column exists whoever owns the other table");
        relations.Should().NotContain("Item { get; set; }",
            "without [ReadAccess] the other side is not in this DbContext, so there is no member to map");

        var includes = SourceEndingWith(result, "Order.Includes");
        includes.Should().Contain(".Include(e => e.Lines)", "the same-boundary navigation is the control");
        includes.Should().NotContain(".Include(e => e.Item)");
    }

    [Fact]
    public void WithReadAccess_TheNavigationCrosses()
    {
        var result = Run("[ReadAccess<CatalogItem>]");

        var relations = SourceEndingWith(result, "Order.Relations");
        relations.Should().Contain("ItemId");
        relations.Should().Contain("Item { get; set; }",
            "the boundary reads CatalogItem, so the navigation is a join EF can make");
    }

    /// <summary>
    ///     ⚠️ The consumer that used the boundary comparison alone: a readable navigation was left out
    ///     of every include, silently, while the member existed.
    /// </summary>
    [Fact]
    public void WithReadAccess_TheNavigationIsIncludable()
    {
        var result = Run("[ReadAccess<CatalogItem>]");

        var includes = SourceEndingWith(result, "Order.Includes");
        includes.Should().Contain(".Include(e => e.Item)",
            "a navigation that exists as a member is loaded like any other");
        includes.Should().Contain(".Include(e => e.Lines)");
    }
}
