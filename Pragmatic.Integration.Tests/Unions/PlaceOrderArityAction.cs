using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Integration.Tests.Domain.Errors;
using Pragmatic.Result;
using ConflictError = Pragmatic.Result.Http.ConflictError;

namespace Pragmatic.Integration.Tests.Unions;

/// <summary>
///     The control for <see cref="PlaceOrderAction" />: the same three errors as an arity, returned as themselves. Whatever the union
///     answers, this is what it must answer.
/// </summary>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "/api/arity-orders")]
public partial class PlaceOrderArityAction : DomainAction<string, NotFoundError, ConflictError, OrderLocked>
{
    /// <summary>The outcome to produce: <c>missing</c>, <c>taken</c>, <c>locked</c>, or anything else for success.</summary>
    [FromQuery]
    public string Outcome { get; init; } = "";

    /// <inheritdoc />
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Outcome switch
        {
            "missing" => Result<string, IError>.Failure(new NotFoundError { ResourceType = "Product", ResourceId = "p-1" }),
            "taken" => Result<string, IError>.Failure(new ConflictError { Reason = "AlreadyExists" }),
            "locked" => Result<string, IError>.Failure(new OrderLocked()),
            _ => Result<string, IError>.Success("o-1"),
        });
}
