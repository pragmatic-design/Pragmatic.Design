using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     What a DTO can reach through when the navigation is generated rather than written.
/// </summary>
/// <remarks>
///     <para>
///         <c>RequiredNavigations</c> is derived from what the DTO names — a flattened path, a nested
///         DTO, a collection of DTOs — and a DTO can only name what the compiler sees. A navigation
///         declared with <c>[Relation.*]</c> is written by another generator and is invisible in the
///         same compilation, so an application that declares its relations that way — one place, on
///         the parent — got an empty list and an auto-include that loaded nothing.
///     </para>
///     <para>
///         Measured on a consumer, not imagined: Shunpo has zero hand-written navigations across every
///         entity that has a relation, and <c>[MapProperty("Workspace.Name")]</c> on its member DTO
///         answers <c>PRAG0302, property not found</c>. The reference application only works because
///         one of its entities keeps the navigation in source, with a comment saying why.
///     </para>
/// </remarks>
public class RequiredNavigationsReachTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        // Declared here and nowhere else, which is the idiomatic way — and which means the Lines
        // collection and the Order back-reference are both written by the persistence generator.
        [Relation.OneToMany<Line>]
        public partial class Order : IEntity
        {
            public string Reference { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Line : IEntity
        {
            public string Description { get; private set; } = "";
        }

        [MapFrom<Line>]
        [GenerateProjection]
        public partial record LineDto
        {
            public Guid Id { get; init; }
            public string Description { get; init; } = "";
        }

        [MapFrom<Order>]
        [GenerateProjection]
        public partial record OrderDetailDto
        {
            public Guid Id { get; init; }
            public string Reference { get; init; } = "";

            /// A collection of DTOs over a navigation this compilation cannot see yet.
            public List<LineDto> Lines { get; init; } = [];
        }
        """;

    /// <summary>
    ///     A DTO over the child can flatten through the back-reference the parent's relation created.
    /// </summary>
    /// <remarks>
    ///     The case that was unreachable. <c>Line</c> declares nothing about <c>Order</c> — the
    ///     back-reference exists because <c>Order</c> declared <c>[Relation.OneToMany&lt;Line&gt;]</c>
    ///     — and reading only the child's own attributes never found it, so
    ///     <c>[MapProperty("Order.Reference")]</c> answered PRAG0302. In an application that declares
    ///     each relation once, on the parent, that is every flattening it could ever write.
    /// </remarks>
    [Fact]
    public void ADtoOverTheChild_CanFlattenThroughTheInverseNavigation()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            {{Source}}

            namespace TestApp
            {
                [MapFrom<Line>]
                [GenerateProjection]
                public partial record LineWithOrderDto
                {
                    public Guid Id { get; init; }

                    [MapProperty("Order.Reference")]
                    public string OrderReference { get; init; } = "";
                }
            }
            """, References());

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0302").Should().BeEmpty(
            "the navigation is generated from the parent's relation, and it is still a navigation");

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "LineWithOrderDto.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("RequiredNavigations { get; } = [\"Order\"];",
            "whoever loads the line has to bring the order with it");
    }

    /// <summary>
    ///     A DTO can write an entity that keeps its state private.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A write path that assigned every property directly would make <c>[MapTo&lt;T&gt;]</c> over
    ///         an entity with <c>private set</c> — the shape this framework recommends, and the one its
    ///         own generator produces <c>Set{Name}</c> for — emit <c>CS0272</c> inside a file the
    ///         author cannot open. A <c>[MapTo]</c> that targets a plain class with public setters never
    ///         shows it.
    ///     </para>
    ///     <para>
    ///         It is not a small corner: a mutation that carries children merges them with
    ///         <c>ToEntity()</c> and <c>ApplyTo()</c> on the element DTO, so without this the whole
    ///         feature would be unusable on any properly encapsulated entity.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ADtoCanWriteAnEntityThatKeepsItsStatePrivate()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            {{Source}}

            namespace TestApp
            {
                [MapTo<Line>]
                public partial class WritableLineDto
                {
                    public Guid Id { get; init; }
                    public string Description { get; init; } = "";
                }
            }
            """, References());

        var errors = GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("WritableLineDto") == true)
            .ToList();
        errors.Should().BeEmpty(string.Join(Environment.NewLine, errors.Select(d => d.ToString())));

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "WritableLineDto.Mapping")!;
        generated.Should().Contain("entity.SetDescription(",
            "the entity exposes a generated setter precisely because the property is not assignable");
    }

    /// <summary>
    ///     A collection of child DTOs is a navigation the read has to bring with it.
    /// </summary>
    [Fact]
    public void ACollectionOfDtos_OverAGeneratedNavigation_IsRequired()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References());

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "OrderDetailDto.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("RequiredNavigations { get; } = [\"Lines\"];",
            "the DTO reaches through Lines, and whoever loads the entity has to know");
    }

    /// <summary>
    ///     What the nested DTO reads is required too, prefixed by the path that reaches it.
    /// </summary>
    /// <remarks>
    ///     The gap this closes: a DTO's own shape says it needs <c>Lines</c>, and says nothing about
    ///     <c>LineDto</c> flattening <c>Product.Name</c>. Loading <c>Lines</c> alone leaves
    ///     <c>Lines.Product</c> unloaded, which resolves to null on a query and throws on a mutation —
    ///     and the DTO that knows about it is not the one being loaded.
    /// </remarks>
    [Fact]
    public void WhatTheNestedDtoReads_IsRequiredToo()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Mapping.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            public class SalesBoundary;

            [Entity] [BelongsTo<SalesBoundary>]
            public partial class Product : IEntity
            {
                public string Name { get; set; } = "";
            }

            [Entity] [BelongsTo<SalesBoundary>]
            public partial class Line : IEntity
            {
                public Product Product { get; set; } = null!;
            }

            [Entity] [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public ICollection<Line> Lines { get; set; } = new List<Line>();
            }

            [MapFrom<Line>]
            public partial class LineDto
            {
                [MapProperty("Product.Name")]
                public string ProductName { get; init; } = "";
            }

            [MapFrom<Order>]
            public partial class OrderDto
            {
                public List<LineDto> Lines { get; init; } = new();
            }
            """, References());

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("\"Lines\"", "the collection itself is still required");
        generated.Should().Contain("\"Lines.Product\"",
            "and so is what the element DTO reads through it — the order has no way to know that on its own");
    }

    /// <summary>
    ///     A DTO that leads back to itself contributes its own paths and stops.
    /// </summary>
    /// <remarks>
    ///     Bounded rather than excluded. Composing the far side's list at runtime would have left two
    ///     static initialisers waiting on each other, so the shape would have had to be skipped
    ///     entirely; walking it in the generator means a cycle costs one visit and still yields the
    ///     navigations that are real.
    /// </remarks>
    [Fact]
    public void ADtoThatLeadsBackToItself_IsBounded()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Mapping.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            public class SalesBoundary;

            [Entity] [BelongsTo<SalesBoundary>]
            public partial class Node : IEntity
            {
                public string Label { get; set; } = "";
                public ICollection<Node> Children { get; set; } = new List<Node>();
            }

            [MapFrom<Node>]
            public partial class NodeDto
            {
                public string Label { get; init; } = "";
                public List<NodeDto> Children { get; init; } = new();
            }
            """, References());

        // The generator terminating and emitting the list is the whole assertion: a walk that did not
        // stop would never reach this line. Not asserted is that the fixture compiles — it declares
        // the minimum the mapping needs, and the generated repository wants references this fixture
        // does not carry, so a compile check here would measure the fixture instead of the walk.
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "NodeDto.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("\"Children\"");
        generated.Should().NotContain("\"Children.Children\"",
            "the branch stops where it turns back on itself");
    }

    private static MetadataReference[] References() =>
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BelongsToAttribute<object>>(),
        GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
        GeneratorTestHelper.FromType<GenerateProjectionAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
        GeneratorTestHelper.FromType<Specification.Specification<object>>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
    ];
}
