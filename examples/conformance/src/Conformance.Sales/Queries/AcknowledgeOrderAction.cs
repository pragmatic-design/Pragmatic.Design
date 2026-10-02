using Conformance.Sales.Entities;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Conformance.Sales.Queries;

/// <summary>
///     An operation that succeeds with nothing to return: <c>204</c>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It is the void operation of conformance: the branch that produces <c>204</c> in the result
///         translation is measured only if some case takes it. A branch no case takes is a line of
///         documentation, not verified behaviour.
///     </para>
///     <para>
///         It is on <c>POST</c> on purpose: the verb alone would give <c>201</c>, and the fact that it
///         answers <c>204</c> shows that «I have no value» comes before the verb in the decision.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/orders/{id}/acknowledge")]
public partial class AcknowledgeOrderAction : VoidDomainAction
{
    private IRepository<Order> _orders = null!;

    public required Guid Id { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(Id, ct).ConfigureAwait(false);

        return order is null
            ? VoidResult<IError>.Failure(NotFoundError.For<Guid>(nameof(Order), Id))
            : Success;
    }
}
