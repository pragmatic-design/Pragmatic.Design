using Conformance.Sales.Dtos;
using Conformance.Sales.Queries;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Results;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     How many orders a reference names, read by <see cref="GuardedOrderCountQuery" /> through
///     <c>[LoadFrom]</c> — whose permission is asked of the caller, as over its own route.
/// </summary>
/// <remarks>
///     The operation itself is open (<c>[AllowAnonymous]</c>), so the only thing that can refuse a caller is
///     the query: <c>[LoadFrom]</c> runs it through its own invoker, not the internal facade that would skip
///     its permission.
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/order-reads/guarded-count")]
public partial class CountGuardedOrdersAction : DomainAction<int, IError>
{
    public required string Reference { get; init; }

    [LoadFrom<GuardedOrderCountQuery>]
    private PagedResult<OrderDto> Orders { get; set; } = null!;

    public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<int, IError>>(Orders.TotalCount);
}
