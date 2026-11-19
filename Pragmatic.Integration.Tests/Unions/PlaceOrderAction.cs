using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Integration.Tests.Domain.Errors;
using Pragmatic.Result;
using ConflictError = Pragmatic.Result.Http.ConflictError;

namespace Pragmatic.Integration.Tests.Unions;

/// <summary>Places an order, or fails with one case of its union error set, as the request asks.</summary>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "/api/union-orders")]
public partial class PlaceOrderAction : DomainAction<string, PlaceOrderError>
{
    /// <summary>The outcome to produce: <c>missing</c>, <c>taken</c>, <c>locked</c>, or anything else for success.</summary>
    [FromQuery]
    public string Outcome { get; init; } = "";

    /// <inheritdoc />
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Outcome switch
        {
            "missing" => Result<string, IError>.Failure(new PlaceOrderError(new NotFoundError { ResourceType = "Product", ResourceId = "p-1" })),
            "taken" => Result<string, IError>.Failure(new PlaceOrderError(new ConflictError { Reason = "AlreadyExists" })),
            "locked" => Result<string, IError>.Failure(new PlaceOrderError(new OrderLocked())),
            _ => Result<string, IError>.Success("o-1"),
        });
}
