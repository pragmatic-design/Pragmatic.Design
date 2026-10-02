using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     A canonical grid request populates a declared query, instead of being applied by hand inside an
///     action.
/// </summary>
/// <remarks>
///     <para>
///         The bridge existed only as an extension over the queryable, so the only form available was a
///         <c>[DomainAction]</c> that fetched <c>Query()</c> and applied the request to it. That form
///         costs more than ergonomics: an action that reads through a repository declares no read, so it
///         is invisible to the article-30 processing register, and it publishes no contract for what the
///         grid may filter on.
///     </para>
///     <para>
///         ⚠️ The request is <b>not</b> a filter over a column. Left to the ordinary rules it became one
///         — a nullable property of a query is a filter by convention — and the generated
///         <c>Apply</c> compared the entity against a field it does not have.
///     </para>
/// </remarks>
public class CanonicalGridFeedsAQueryTests
{
    private const string Entity = """
        using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Adapters;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        [Entity]
        [GenerateGridBridge]
        public partial class Order : IEntity
        {
            [Filterable]
            public string Code { get; private set; } = "";
            [Filterable]
            public decimal Amount { get; private set; }
        }
        """;

    /// <summary>The query names the request, and the generated <c>Apply</c> feeds it to the bridge.</summary>
    [Fact]
    public void AQueryDeclaringACanonicalRequest_AppliesItThroughTheBridge()
    {
        var result = Run(Entity + """

            [Query<Order, Order>]
            public partial class OrderGridQuery
            {
                public GridFilterRequest? Grid { get; init; }
            }
            """);

        GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.ToString().Contains("TestSource"))
            .Should().BeEmpty("the query as the author wrote it has to compile");

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "OrderGridQuery.Query");

        generated.Should().NotBeNull("the query still generates its Apply");
        generated!.Should().Contain("OrderGridFilterBridge.ApplyCanonical",
            "the request is applied through the entity's generated bridge");
    }

    /// <summary>
    ///     And it is not compared against a column.
    /// </summary>
    /// <remarks>
    ///     The control for the case above: "the request reaches the query" would be satisfied by a
    ///     generated <c>Where</c> naming the property, which is what the ordinary filter rules produced
    ///     and which does not compile.
    /// </remarks>
    [Fact]
    public void TheRequest_IsNotTurnedIntoAColumnComparison()
    {
        var result = Run(Entity + """

            [Query<Order, Order>]
            public partial class OrderGridQuery
            {
                public GridFilterRequest? Grid { get; init; }
            }
            """);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "OrderGridQuery.Query");

        generated.Should().NotBeNull();
        generated!.Should().NotContain("e.Grid",
            "the request is not a column of the entity");
    }

    /// <summary>The other half of the pair: a query that declares no request names no bridge.</summary>
    [Fact]
    public void AQueryWithoutOne_DoesNotNameTheBridge()
    {
        var result = Run(Entity + """

            [Query<Order, Order>]
            public partial class OrderCodeQuery
            {
                public string? Code { get; init; }
            }
            """);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "OrderCodeQuery.Query");

        generated.Should().NotBeNull();
        generated!.Should().NotContain("ApplyCanonical",
            "otherwise 'the request is applied' would be satisfied by applying one that is never there");
    }

    /// <summary>
    ///     An entity that declares no bridge is reported, on the query, before the generated file fails.
    /// </summary>
    /// <remarks>
    ///     <c>[GenerateGridBridge]</c> is the bridge's only trigger, and the generated <c>Apply</c> calls
    ///     the bridge by name: without the attribute the failure is a <c>CS0103</c> inside
    ///     <c>{Query}.Query.g.cs</c>, on a line the author never wrote, naming a type they have never
    ///     heard of.
    /// </remarks>
    [Fact]
    public void AnEntityWithoutTheBridge_IsReported()
    {
        var result = Run("""
            using System;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Adapters;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [Entity]
            public partial class Ticket : IEntity
            {
                public string Code { get; private set; } = "";
            }

            [Query<Ticket, Ticket>]
            public partial class TicketGridQuery
            {
                public GridFilterRequest? Grid { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0723").Should().BeTrue(
            "the query asks for a bridge nobody declared");
    }

    /// <summary>The control: with the attribute there is a bridge, and nothing is reported.</summary>
    [Fact]
    public void AnEntityWithTheBridge_IsSilent()
    {
        var result = Run(Entity + """

            [Query<Order, Order>]
            public partial class OrderGridQuery
            {
                public GridFilterRequest? Grid { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0723").Should().BeFalse();
    }

    /// <summary>
    ///     A query that pages beside a request carrying its own page is reported.
    /// </summary>
    /// <remarks>
    ///     The bridge applies the request's paging inside <c>Apply</c>, and the executor applies the
    ///     query's on top of it: the second skip counts from the first page's rows, so page 2 of 2
    ///     answers nothing at all.
    /// </remarks>
    [Fact]
    public void ARequestBesideItsOwnPaging_IsReported()
    {
        var result = Run(Entity + """

            [Query<Order, Order>]
            public partial class OrderPagedGridQuery
            {
                public GridFilterRequest? Grid { get; init; }
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 20;
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0724").Should().BeTrue(
            "the request carries a page and the query declares one too");
    }

    /// <summary>The control: a request without the query's own paging pages once.</summary>
    [Fact]
    public void ARequestWithoutQueryPaging_IsSilent()
    {
        var result = Run(Entity + """

            [Query<Order, Order>]
            public partial class OrderGridQuery
            {
                public GridFilterRequest? Grid { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0724").Should().BeFalse();
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References());

    private static MetadataReference[] References()
    {
        return
        [
            GeneratorTestHelper.FromType<IEntity>(),
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<GenerateGridBridgeAttribute>(),
            GeneratorTestHelper.FromType<FilterOperator>(),
            GeneratorTestHelper.FromType<SortDirection>(),
            GeneratorTestHelper.FromType<GridFilterRequest>(),
            GeneratorTestHelper.FromType<FilterClause>(),
            GeneratorTestHelper.FromType<Specification.Specification<object>>(),
            GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Linq.Queryable)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Linq.Expressions.Expression)),
            GeneratorTestHelper.FromTypeAssembly(typeof(GridFieldRejectedException)),
        ];
    }
}
