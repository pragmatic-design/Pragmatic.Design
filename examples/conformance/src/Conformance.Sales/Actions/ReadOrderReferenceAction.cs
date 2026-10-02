using Conformance.Sales.Dtos;
using Conformance.Sales.Queries;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     The reference of an order, read by the declared <see cref="GetOrderQuery" /> — <c>[LoadFrom]</c>, a
///     query as the source of an operation's data.
/// </summary>
/// <remarks>
///     A <c>Single</c> query that finds nothing fails the operation with its 404, before the body runs.
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/order-reads/reference")]
public partial class ReadOrderReferenceAction : DomainAction<string, IError>
{
    public required Guid Id { get; init; }

    [LoadFrom<GetOrderQuery>]
    private OrderDto? Order { get; set; }

    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<string, IError>>(Order!.Reference);
}
