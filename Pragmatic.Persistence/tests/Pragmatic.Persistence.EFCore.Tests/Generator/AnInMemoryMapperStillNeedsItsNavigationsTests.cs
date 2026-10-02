using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     A query that maps in memory still loads the navigations it declares.
/// </summary>
/// <remarks>
///     <para>
///         <c>NeedsEagerLoading</c> was gated on <c>IsSameEntityAndResult</c>, and the reasoning was
///         right for the case it was written for: a query answering with a DTO <b>projects</b>, EF
///         turns a flattened path into a JOIN, and an include there adds nothing.
///     </para>
///     <para>
///         ⚠️ <c>MapInMemory</c> is the one shape that answers with a DTO and does <b>not</b> project
///         — that is why it exists. So the gate silently dropped every path it declared, the mapper
///         ran over entities whose navigations were empty, and the mapped property came back null.
///         The condition is about projection, not about identity of entity and result.
///     </para>
///     <para>
///         It stayed invisible because no example declares <c>MapInMemory</c> and neither of the two
///         tests that did declared a navigation path — so nothing in the repository wrote the pair.
///     </para>
/// </remarks>
public class AnInMemoryMapperStillNeedsItsNavigationsTests
{
    private const string Domain = """
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Code { get; private set; } = "";

            /// <summary>Declared by hand: what [EagerLoad] names.</summary>
            public Customer Customer { get; private set; } = null!;
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Customer : IEntity
        {
            public string Name { get; private set; } = "";
        }

        // [MapFrom] alone: FromEntity and Selector, which run in memory. No Projection.
        [MapFrom<Order>]
        public partial class OrderRow
        {
            public string Code { get; set; } = "";
        }

        [MapFrom<Order>]
        [GenerateProjection]
        public partial class OrderDto
        {
            public string Code { get; set; } = "";
        }
        """;

    [Fact]
    public void AMapInMemoryQuery_ListsTheNavigationsItDeclares()
    {
        var generated = Generated("""
            [Query<Order, OrderRow>(MapInMemory = true)]
            [EagerLoad("Customer")]
            public partial class ListOrdersQuery
            {
                [Filter]
                public string? Code { get; init; }
            }
            """);

        generated.Should().Contain("IncludePaths => [\"Customer\"",
            "the mapper reads the entity's navigations in memory, so the include is not an "
            + "optimisation here — it is the difference between a name and a null");
        generated.Should().Contain("IIncludableQuery",
            "IncludePaths reaches the executor through the interface, and PrepareSource applies it "
            + "before Apply runs");
        generated.Should().Contain("MapEach",
            "the two compose: the includes are their own member, and the query still maps in memory");
    }

    /// <summary>
    ///     The first control: a query with nothing to load declares nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "a MapInMemory query lists its paths" is satisfied by giving every one of them
    ///     an empty <c>IncludePaths</c> and an interface it does not need.
    /// </remarks>
    [Fact]
    public void AMapInMemoryQueryWithNoPaths_DeclaresNone()
    {
        var generated = Generated("""
            [Query<Order, OrderRow>(MapInMemory = true)]
            public partial class ListOrdersQuery
            {
                [Filter]
                public string? Code { get; init; }
            }
            """);

        generated.Should().NotContain("IncludePaths",
            "there is nothing to load, and an empty list would still make the executor walk it");
        generated.Should().NotContain("IIncludableQuery",
            "the interface is the claim that this query has navigations to bring back");
    }

    /// <summary>
    ///     The second control: a <b>projecting</b> query still declares nothing, and that is on purpose.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the case the original gate was written for, and it has not changed: the
    ///     projection is applied as a <c>Select</c>, EF turns a flattened path into a JOIN, and an
    ///     include is dropped the moment the query stops returning the entity. Widening the gate to
    ///     every query would have been the easy fix and would have added a member that does nothing.
    /// </remarks>
    [Fact]
    public void AProjectingQuery_StillDeclaresNone()
    {
        var generated = Generated("""
            [Query<Order, OrderDto>]
            [EagerLoad("Customer")]
            public partial class ListOrdersQuery
            {
                [Filter]
                public string? Code { get; init; }
            }
            """);

        generated.Should().Contain("Projection", "this query projects, which is the default");
        generated.Should().NotContain("IncludePaths",
            "EF Core drops an Include once the query no longer returns the entity, so declaring one "
            + "here would be a member that changes nothing");
    }

    private static string Generated(string query)
    {
        var result = Run(Domain + "\n" + query);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "ListOrdersQuery.Query");
        generated.Should().NotBeNull("the query generates its own file whatever it declares");
        return generated!;
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References());

    private static MetadataReference[] References() =>
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BelongsToAttribute<object>>(),
        GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
        // [EagerLoad] lives in Pragmatic.Actions: without it the `using` fails, the whole compilation
        // is an error, and the generator emits nothing — which reads as "the feature produced nothing".
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Mutation.EagerLoadAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
        GeneratorTestHelper.FromType<Specification.Specification<object>>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
    ];
}
