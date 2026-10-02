using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A declared query is a member of its boundary's interface, like every other operation.
/// </summary>
/// <remarks>
///     <para>
///         Without the member, invoking a query from code takes three pieces — build the object, get a
///         source, pick the executor overload — and applications do not do it. A read that another
///         module cannot call by name is a read that module will rewrite by hand.
///     </para>
///     <para>
///         ⚠️ The member invokes the query's <b>invoker</b>, not the executor. Naming the executor here
///         would put the three pieces behind a name instead of removing them, and would skip the
///         validation and the permission.
///     </para>
///     <para>
///         ⚠️ The internal/public split applies, as it does for actions: the public
///         interface enforces the invoked operation's permission, the internal one is the internal call.
///         A query member follows the same rule, or the boundary absorbs a query's permission.
///     </para>
/// </remarks>
public class TheQueryIsAMemberOfItsBoundaryTests
{
    private const string Source = """
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Authorization;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Shop.Sales;

        [Boundary]
        public partial class SalesBoundary;

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

    private static string Definition()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source);

        var definition = sources
            .Where(pair => pair.Key.EndsWith("_Boundary.SalesBoundary.Definition.g.cs"))
            .Select(pair => pair.Value)
            .FirstOrDefault();

        definition.Should().NotBeNull("the boundary declares operations, so it has a facade");
        return definition!;
    }

    private static string Local()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source);

        var local = sources
            .Where(pair => pair.Key.EndsWith("_Boundary.SalesBoundary.Local.g.cs"))
            .Select(pair => pair.Value)
            .FirstOrDefault();

        local.Should().NotBeNull("the facade has a local implementation");
        return local!;
    }

    /// <summary>The query is named on the interface another module compiles against.</summary>
    [Fact]
    public void TheQuery_IsAMemberOfTheBoundaryInterface()
    {
        var definition = Definition();

        definition.Should().Contain("SearchOrders",
            "a read nobody can call by name is a read that gets rewritten by hand");
        definition.Should().Contain("SearchOrdersQuery");
    }

    /// <summary>The member answers what the query answers.</summary>
    /// <remarks>
    ///     A paged query answers a <c>PagedResult</c>, and the member's return type has to say so — the
    ///     caller reads <c>Items</c> and <c>TotalCount</c> off it.
    /// </remarks>
    [Fact]
    public void TheMember_AnswersTheQuerysShape()
    {
        var definition = Definition();

        definition.Should().Contain("PagedResult<global::Shop.Sales.Order>",
            "the member answers the page the query answers");
    }

    /// <summary>The query's inputs become parameters, as an action's do.</summary>
    [Fact]
    public void TheMember_TakesTheQuerysInputsUnwrapped()
    {
        var definition = Definition();

        definition.Should().Contain("code",
            "the unwrapped overload is what makes the facade worth calling");
        definition.Should().Contain("global::System.String? code",
            "⚠️ qualified: this signature is emitted into the boundary's file, which has none of the "
            + "author's using directives — an unqualified SortDirection? in the same position is what "
            + "stopped the Showcase from compiling");
    }

    /// <summary>The implementation goes through the query's invoker.</summary>
    /// <remarks>
    ///     ⚠️ Not through <c>IQueryExecutor</c>: that would put the three pieces behind a name and skip
    ///     both the validation and the permission — the defect this whole epic exists to close.
    /// </remarks>
    [Fact]
    public void TheImplementation_GoesThroughTheInvoker()
    {
        var local = Local();

        local.Should().Contain("SearchOrdersQuery.Invoker");
        local.Should().NotContain("IQueryExecutor",
            "the facade invokes the operation, it does not execute the read itself");
    }

    /// <summary>
    ///     The internal interface enters the internal call; the public one does not.
    /// </summary>
    /// <remarks>
    ///     The same pair actions have. Without it the facade would absorb the query's
    ///     permission — every caller of the boundary would read what only some callers may read.
    /// </remarks>
    [Fact]
    public void ThePublicFacade_DoesNotEnterTheInternalCall()
    {
        var local = Local();

        var publicImpl = local[..local.IndexOf("Internal", System.StringComparison.Ordinal)];

        publicImpl.Should().NotContain("EnterInternalCall",
            "the public facade enforces the invoked operation's permission");
    }
}
