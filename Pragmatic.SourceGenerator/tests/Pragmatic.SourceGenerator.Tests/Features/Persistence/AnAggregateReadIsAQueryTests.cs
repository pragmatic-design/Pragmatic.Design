using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A grouping is a read of the same kind: a <c>[Query]</c> whose result is an aggregate view gets
///     the route, the permission and the invoker every other read has.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The declarative half alone stops one step early. <c>[QueryView&lt;T&gt;]</c> with
///         <c>[GroupBy]</c> and <c>[Count]/[Sum]/[Avg]</c> generates a <c>Build(IQueryable&lt;T&gt;)</c>
///         that groups in SQL — and there it ends: a view has no route and no entry in the published
///         contract, so without a query exposing an aggregate means a hand-written action holding a
///         repository.
///     </para>
///     <para>
///         What a query cannot do is project a group: <c>Projection</c> is
///         <c>Expression&lt;Func&lt;TEntity, TResult&gt;&gt;</c>, one row in and one row out, and after a
///         <c>GroupBy</c> the source is no longer rows of the entity. So the aggregate travels as a
///         third alternative beside <c>Projection</c> and <c>MapEach</c>: a function from the filtered
///         set to the projected set, which is exactly the shape <c>Build</c> already has.
///     </para>
/// </remarks>
public class AnAggregateReadIsAQueryTests
{
    private const string Common = """
        using System;
        using System.Collections.Generic;
        using Pragmatic.Authorization;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public sealed class SalesBoundary { }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public Guid Id { get; set; }
            public Guid PersistenceId { get => Id; set => Id = value; }
            public string Status { get; set; } = "";
            public decimal Amount { get; set; }
        }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }

        [QueryView<Order>]
        [GroupBy<Order>(Properties = "Status")]
        public partial class OrdersByStatusView
        {
            [From<Order>]
            public string Status { get; init; } = "";

            [Count<Order>]
            public int Orders { get; init; }

            [Sum<Order>(Expression = "Amount")]
            public decimal Total { get; init; }
        }
        """;

    private const string AggregateQuery = """

        [Query<Order, OrdersByStatusView>]
        [Endpoint(HttpVerb.Get, "api/orders/by-status")]
        [RequirePermission("sales.order.read")]
        public partial class OrdersByStatusQuery
        {
            public string? Status { get; set; }
        }
        """;

    /// <summary>The query hands the grouping to the view that declares it.</summary>
    [Fact]
    public void AQueryOverAnAggregateView_ProjectsThroughTheViewsBuild()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + AggregateQuery);

        var query = Generated(sources, "OrdersByStatusQuery.Query");

        query.Should().NotBeNull("a query over an aggregate view must still be generated");
        query!.Should().Contain("Aggregate",
            "a group cannot travel as a row-to-row projection");
        query.Should().Contain("OrdersByStatusView.Build",
            "the grouping is already declared on the view; the query does not restate it");
    }

    /// <summary>
    ///     And it does not claim a projection it cannot have.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The control that matters. Without it the query would emit
    ///     <c>Projection =&gt; OrdersByStatusView.Projection</c> — a member the view does not have — and
    ///     the failure would be a CS0117 inside generated code, blamed on the view.
    /// </remarks>
    [Fact]
    public void AQueryOverAnAggregateView_DoesNotClaimARowProjection()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + AggregateQuery);

        var query = Generated(sources, "OrdersByStatusQuery.Query");

        query.Should().NotBeNull();
        query!.Should().NotContain("OrdersByStatusView.Projection");
        query.Should().NotContain("OrdersByStatusView.Selector");
    }

    /// <summary>
    ///     The control: an ordinary query is untouched, and still projects row by row.
    /// </summary>
    /// <remarks>
    ///     Without it, "the aggregate path is taken" is satisfied by taking it for every query, which
    ///     would break every declared read in the repository.
    /// </remarks>
    [Fact]
    public void AnOrdinaryQuery_StillProjectsRowByRow()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + """

            public partial class OrderRow
            {
                public string Status { get; init; } = "";
            }

            [Query<Order, OrderRow>]
            [Endpoint(HttpVerb.Get, "api/orders/rows")]
            public partial class OrderRowsQuery
            {
                public string? Status { get; set; }
            }
            """);

        var query = Generated(sources, "OrderRowsQuery.Query");

        query.Should().NotBeNull();
        query!.Should().NotContain("Aggregate", "nothing is grouped here");
    }

    /// <summary>The read reaches HTTP: the aggregate query gets the same invoker as any other.</summary>
    /// <remarks>
    ///     A declarative grouping alone has no route, no permission and no pipeline; the query is what
    ///     gives it all three, so an aggregate does not need a hand-written action.
    /// </remarks>
    [Fact]
    public void TheAggregateRead_GetsTheInvokerAndThePermission()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + AggregateQuery);

        var invoker = Generated(sources, "OrdersByStatusQuery.QueryInvoker");

        invoker.Should().NotBeNull("an aggregate read is a read, with the same pipeline");
        invoker!.Should().Contain("\"sales.order.read\"");
        invoker.Should().Contain("ExecuteAllAsync", "a grouping that declares no paging answers a list");
    }

    /// <summary>
    ///     The diagnostic that guards the projection does not refuse an aggregate.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>PRAG0704</c> exists because a result type without a <c>Projection</c> makes the
    ///     generated <c>Apply</c> name a member that does not exist. A view has no projection for a
    ///     reason — a grouping cannot be one — so without the exemption the read could be declared and
    ///     the module would not build.
    /// </remarks>
    [Fact]
    public void AnAggregateView_IsNotReportedAsHavingNoProjection()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + AggregateQuery);

        diagnostics.Should().NotContain(d => d.Id == "PRAG0704",
            "the view declares the whole step instead of a row projection");
    }

    /// <summary>
    ///     The control: a plain result type that declares no projection is still reported.
    /// </summary>
    /// <remarks>
    ///     Without it, "the aggregate is exempt" would be satisfied by removing the diagnostic, and the
    ///     error it prevents would come back as a CS0117 inside a file the author cannot edit.
    /// </remarks>
    [Fact]
    public void APlainResultWithoutAProjection_IsStillReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + """

            public partial class BareRow
            {
                public string Status { get; init; } = "";
            }

            [Query<Order, BareRow>]
            [Endpoint(HttpVerb.Get, "api/orders/bare")]
            public partial class BareRowsQuery
            {
                public string? Status { get; set; }
            }
            """);

        diagnostics.Should().Contain(d => d.Id == "PRAG0704",
            "a row projection that does not exist is still an error the author must be told about");
    }

    private static string? Generated(Dictionary<string, string> sources, string hintPart)
        => sources.FirstOrDefault(s => s.Key.Contains(hintPart)).Value;
}
