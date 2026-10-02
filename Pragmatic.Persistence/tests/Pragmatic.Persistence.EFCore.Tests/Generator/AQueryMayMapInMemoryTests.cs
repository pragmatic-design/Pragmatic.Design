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
///     A declared query may answer a DTO that maps <b>in memory</b>, when the author says so.
/// </summary>
/// <remarks>
///     <para>
///         A query's result type had to project into SQL: the generated <c>Apply</c> names
///         <c>{TResult}.Projection</c>, which exists only under <c>[GenerateProjection]</c>, and
///         anything else is <c>PRAG0704</c>. A grid row does not project — it joins two columns, runs a
///         converter, formats a date — and that is the read shape a line-of-business application has
///         the most of. "Declare your reads" excluded it.
///     </para>
///     <para>
///         ⚠️ <b>Declared, never inferred.</b> The absence of <c>[GenerateProjection]</c> reads as an
///         omission, not a decision, and a silent materialisation is the performance trap the
///         projection exists to avoid. So the author writes <c>MapInMemory = true</c>, and the
///         generated code says which of the two it does.
///     </para>
///     <para>
///         Filtering, sorting and paging stay server-side: only the projection moves. The executor
///         materialises the page it was going to return anyway, and maps that.
///     </para>
/// </remarks>
public class AQueryMayMapInMemoryTests
{
    private const string Entity = """
        using System;
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
            public string City { get; private set; } = "";
        }

        // [MapFrom] alone: FromEntity and Selector, which run in memory. No Projection.
        [MapFrom<Order>]
        public partial class OrderRow
        {
            public string Code { get; set; } = "";
            public string Label => Code + " · " + City;
            public string City { get; set; } = "";
        }
        """;

    /// <summary>The claim: it is legal, and the generated query says it maps rather than projects.</summary>
    [Fact]
    public void AQueryDeclaringIt_MapsInsteadOfProjecting()
    {
        var result = Run(Entity + """

            [Query<Order, OrderRow>(MapInMemory = true)]
            public partial class ListOrdersQuery
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? City { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0704").Should().BeFalse(
            "the result type is not required to project when the query says it maps in memory");

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "ListOrdersQuery.Query");

        generated.Should().NotBeNull();
        generated!.Should().Contain("MapEach",
            "the executor has to be told which of the two paths this query takes");
        generated.Should().NotContain("OrderRow.Projection",
            "naming a member the result type does not have is what PRAG0704 exists to prevent");
    }

    /// <summary>
    ///     The control: without the declaration, the requirement stands.
    /// </summary>
    /// <remarks>
    ///     Without it, "a query may map in memory" would be satisfied by letting every query do so —
    ///     which turns a projected read into a materialised one silently, entity by entity, and that is
    ///     the trap the projection exists to avoid.
    /// </remarks>
    [Fact]
    public void AQueryNotDeclaringIt_StillNeedsAProjection()
    {
        var result = Run(Entity + """

            [Query<Order, OrderRow>]
            public partial class ListOrdersQuery
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? City { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0704").Should().BeTrue(
            "the generated Apply would name OrderRow.Projection, and that member does not exist");
    }

    /// <summary>The second control: a result type that projects is unaffected.</summary>
    [Fact]
    public void AProjectingResultType_KeepsProjecting()
    {
        var result = Run("""
            using System;
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
            }

            [MapFrom<Order>]
            [GenerateProjection]
            public partial class OrderDto
            {
                public string Code { get; set; } = "";
            }

            [Query<Order, OrderDto>]
            public partial class ListOrdersQuery
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? Code { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0704").Should().BeFalse();

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "ListOrdersQuery.Query");

        generated.Should().NotBeNull();
        generated!.Should().Contain("Projection",
            "the SQL projection is still the default, and still what a query without the option does");
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
        GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
        GeneratorTestHelper.FromType<Specification.Specification<object>>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
    ];
}
