using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Authorization;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Queries;

/// <summary>
///     A query that declares a permission and a validation rule, to measure the pipeline every caller
///     goes through.
/// </summary>
/// <remarks>
///     <para>
///         Without the invoker the two declarations would hold only on the route: reached in-process,
///         the query would validate nothing and ask nobody for anything. The other queries of this module
///         declare neither, so they cannot tell a pipeline that runs from one that is not there.
///     </para>
///     <para>
///         Demonstrates: the input of a declared query goes through the same validation as an action's,
///         and its permission holds on both doors.
///     </para>
/// </remarks>
[Query<Order, OrderDto>]
[RequirePermission("conformance.order.guardedread")]
[Endpoint(HttpVerb.Get, "api/orders/guarded")]
public partial class GuardedOrderCountQuery
{
    /// <summary>The reference searched for, which cannot be empty.</summary>
    /// <remarks>
    ///     ⚠️ The rule is Pragmatic's <c>[MinLength]</c>, not the DataAnnotations one: PRAG0210 exists
    ///     because the latter generates no check, and a case written with it would stay green on a query
    ///     that validates nothing.
    /// </remarks>
    [MinLength(3)]
    [Filter]
    public string Reference { get; init; } = "";

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
