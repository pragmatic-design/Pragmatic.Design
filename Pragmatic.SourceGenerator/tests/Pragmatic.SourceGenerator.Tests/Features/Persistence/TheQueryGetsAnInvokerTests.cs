using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A declared query gets a generated invoker, and its route goes through it.
/// </summary>
/// <remarks>
///     <para>
///         The runtime half — <c>QueryInvoker&lt;TQuery&gt;</c>, validation then permission — landed
///         with nothing deriving from it. This is what makes it real: a nested <c>Invoker</c> per
///         query, and the generated handler asking it for the answer instead of resolving
///         <c>IQueryExecutor</c> and the keyed context itself.
///     </para>
///     <para>
///         ⚠️ The two are one change on purpose. An invoker nobody calls would leave two ways to invoke
///         one operation — HTTP enforcing the permission on the route builder, in-process enforcing it
///         in the invoker — which is the shape the boundary interface does not have.
///     </para>
///     <para>
///         ⚠️ The source stays the <b>raw</b> set: <c>GetRequiredKeyedService&lt;DbContext&gt;</c> then
///         <c>Set&lt;TEntity&gt;()</c>, never <c>repository.Query()</c>. <c>Query()</c> is already
///         <c>ApplyFilters(Set)</c> and <c>ApplyFilters</c> calls <c>IgnoreQueryFilters</c>, of which EF
///         keeps the last in the chain — feeding an already-filtered source to the executor would
///         change which filters are active rather than repeat them.
///     </para>
///     <para>
///         Runs through <see cref="TraitCompilationHarness" /> because the change crosses two features:
///         the invoker is Persistence's, the handler is Endpoints'. Its name says trait; what it does is
///         run the whole generator with the whole reference closure.
///     </para>
/// </remarks>
public class TheQueryGetsAnInvokerTests
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
        using Pragmatic.Validation.Attributes;

        namespace TestApp;

        public sealed class SalesBoundary { }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public Guid Id { get; set; }
            public Guid PersistenceId { get => Id; set => Id = value; }
            public string Code { get; set; } = "";
        }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }
        """;

    private const string PagedQuery = """

        [Query<Order, Order>]
        [Endpoint(HttpVerb.Get, "api/orders")]
        [RequirePermission("sales.order.read")]
        public partial class SearchOrdersQuery
        {
            public string? Code { get; set; }
            public int Page { get; set; } = 1;
            public int PageSize { get; set; } = 20;
        }
        """;

    /// <summary>The query's own type gains an <c>Invoker</c>.</summary>
    [Fact]
    public void ADeclaredQuery_GetsAnInvoker()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + PagedQuery);

        var invoker = Invoker(sources, "SearchOrdersQuery");

        invoker.Should().NotBeNull("a query with no invoker has no pipeline of its own");
        invoker!.Should().Contain("class Invoker");
        invoker.Should().Contain("QueryInvoker<",
            "the pipeline is the shared one, not a second copy of validation and permission");
    }

    /// <summary>The read inside the invoker starts from the raw set of the keyed context.</summary>
    /// <remarks>
    ///     Named here rather than left to the endpoint's old shape: this is the one line where handing
    ///     the executor <c>repository.Query()</c> instead would silently change which query filters are
    ///     active.
    /// </remarks>
    [Fact]
    public void TheInvokersRead_StartsFromTheRawSet()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + PagedQuery);

        var invoker = Invoker(sources, "SearchOrdersQuery");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("GetRequiredKeyedService", "the source is the boundary's context");
        invoker.Should().Contain("typeof(global::TestApp.SalesBoundary)");
        invoker.Should().Contain("Set<global::TestApp.Order>()");
        invoker.Should().NotContain(".Query()",
            "an already-filtered source would change the filters, not repeat them");
    }

    /// <summary>The route stops reading by itself and asks the invoker.</summary>
    /// <remarks>
    ///     The half that makes the two paths one. Without it the invoker would be a second door with its
    ///     own rules, which is worse than the single door that exists today.
    /// </remarks>
    [Fact]
    public void TheRoute_DelegatesToTheInvoker()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + PagedQuery);

        var endpoint = Endpoint(sources, "SearchOrdersQuery");

        endpoint.Should().NotBeNull();
        endpoint!.Should().Contain("Invoker", "the handler asks the invoker for the answer");
        endpoint.Should().NotContain("executor.ExecuteAsync",
            "the handler no longer runs the read itself");
    }

    /// <summary>A single-result query gets an invoker of the single shape.</summary>
    /// <remarks>
    ///     Three shapes exist — one row, a page, a list — and an invoker generated for only the paged
    ///     one would compile and answer the wrong thing for the other two.
    /// </remarks>
    [Fact]
    public void ASingleResultQuery_GetsTheSingleShape()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + """

            [Query<Order, Order>(Single = true)]
            [Endpoint(HttpVerb.Get, "api/orders/{code}")]
            public partial class GetOrderQuery
            {
                public string Code { get; set; } = "";
            }
            """);

        var invoker = Invoker(sources, "GetOrderQuery");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("ExecuteSingleAsync", "Single = true asks for one row");
    }

    /// <summary>A list query — neither paged nor single — gets the list shape.</summary>
    [Fact]
    public void AListQuery_GetsTheListShape()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + """

            [Query<Order, Order>]
            [Endpoint(HttpVerb.Get, "api/orders/all")]
            public partial class AllOrdersQuery
            {
                public string? Code { get; set; }
            }
            """);

        var invoker = Invoker(sources, "AllOrdersQuery");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("ExecuteAllAsync",
            "a query that declares no paging answers with a list");
    }

    /// <summary>The permission declared on the query is what the invoker enforces.</summary>
    /// <remarks>
    ///     ⚠️ Written into the invoker, not looked up in <c>IPermissionRequirementRegistry</c>. That
    ///     registry is built from an assembly's actions and mutations and has never carried a query:
    ///     the first version of this case asserted the query appeared in it, and the red said plainly
    ///     that it never would. An invoker asking it would be told nothing is required and would admit
    ///     every caller — a permission step indistinguishable from no step at all.
    /// </remarks>
    [Fact]
    public void ThePermissionOnTheQuery_ReachesItsInvoker()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + PagedQuery);

        var invoker = Invoker(sources, "SearchOrdersQuery");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("RequiredPermissions");
        invoker.Should().Contain("\"sales.order.read\"");
    }

    /// <summary>The other half: a query that declares no permission enforces none.</summary>
    /// <remarks>
    ///     Without it "the permission is enforced" would be satisfied by an invoker that refuses
    ///     everyone, and every query in the repository that declares nothing would start answering 403.
    /// </remarks>
    [Fact]
    public void AQueryDeclaringNoPermission_EnforcesNone()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + """

            [Query<Order, Order>]
            [Endpoint(HttpVerb.Get, "api/orders/open")]
            public partial class OpenOrdersQuery
            {
                public string? Code { get; set; }
            }
            """);

        var invoker = Invoker(sources, "OpenOrdersQuery");

        invoker.Should().NotBeNull();
        invoker!.Should().NotContain("RequiredPermissions",
            "a query that declares nothing has nothing to enforce");
    }

    /// <summary>A permission that binds to nothing is reported, not dropped.</summary>
    /// <remarks>
    ///     ⚠️ The invoker enforces the permissions it was written with, so a name that did not bind is a
    ///     name it cannot enforce: the query would run its permission step, find nothing to require, and
    ///     answer every caller. That is worse than declaring nothing, because the declaration reads as
    ///     protection — which is why <c>PRAG0725</c> is an error rather than a silent omission.
    /// </remarks>
    [Fact]
    public void APermissionThatBindsToNothing_IsReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + """

            [Query<Order, Order>]
            [Endpoint(HttpVerb.Get, "api/orders/unbound")]
            [RequirePermission(SomewhereElsePermissions.Order.Read)]
            public partial class UnboundPermissionQuery
            {
                public string? Code { get; set; }
            }
            """);

        diagnostics.Should().Contain(d => d.Id == "PRAG0725",
            "a permission the invoker cannot enforce is not a permission");
    }

    /// <summary>The control: a permission that binds is silent.</summary>
    /// <remarks>
    ///     Without it the diagnostic would be satisfied by reporting every query that declares one,
    ///     which would forbid the shape it exists to protect.
    /// </remarks>
    [Fact]
    public void APermissionThatBinds_IsSilent()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + PagedQuery);

        diagnostics.Should().NotContain(d => d.Id == "PRAG0725",
            "a literal always binds");
    }

    /// <summary>The generated invoker of the named query, if there is one.</summary>
    private static string? Invoker(Dictionary<string, string> sources, string queryTypeName)
        => sources
            .Where(pair => pair.Key.Contains(queryTypeName) && pair.Key.EndsWith(".QueryInvoker.g.cs"))
            .Select(pair => pair.Value)
            .FirstOrDefault();

    /// <summary>The generated endpoint of the named query, if there is one.</summary>
    private static string? Endpoint(Dictionary<string, string> sources, string queryTypeName)
        => sources
            .Where(pair => pair.Key.Contains(queryTypeName) && pair.Key.EndsWith(".Endpoint.g.cs"))
            .Select(pair => pair.Value)
            .FirstOrDefault();
}
