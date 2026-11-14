using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Queries;

/// <summary>
///     Orders by their notes, the ones without notes last whatever the direction (#733): <c>[Sort(Nulls = NullOrder.Last)]</c> on EF Core.
/// </summary>
/// <remarks>
///     Exists to pin the null order on a real database: PostgreSQL puts nulls first in a descending sort, so without the declaration the
///     orders without notes come first when the caller sorts descending.
/// </remarks>
[Query<Order, OrderDto>]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "api/orders/by-notes")]
public partial class ListOrdersByNotesQuery
{
    [Filter(Operator = FilterOperator.StartsWith)]
    public string? Reference { get; init; }

    [Sort(MapTo = nameof(Order.Notes), DefaultDirection = SortDirection.Ascending, Nulls = NullOrder.Last)]
    public SortDirection? NotesSort { get; init; }
}
