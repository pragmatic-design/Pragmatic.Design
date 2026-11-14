using Conformance.Sales.Entities;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     The number of lines of each order a list of keys names — the rows read by <c>[LoadEntities(Include = "Lines")]</c>, their lines with
///     them (#730).
/// </summary>
/// <remarks>
///     Exists to pin the list load with navigations on a real database: there is no lazy loading, so without the <c>Include</c> every order's
///     <c>Lines</c> is empty and every count is 0.
/// </remarks>
[DomainAction]
[AllowAnonymous]
[LoadEntities<Order>(nameof(OrderIds), Include = "Lines")]
[Endpoint(HttpVerb.Post, "api/orders/line-counts")]
public partial class CountLinesOfOrdersAction : DomainAction<int[], IError>
{
    public required IReadOnlyList<Guid> OrderIds { get; init; }

    public override Task<Result<int[], IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<int[], IError>>(_orders.Select(order => order.Lines.Count).ToArray());
}
