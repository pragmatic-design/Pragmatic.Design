using Conformance.Sales.Infrastructure.Services;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     A <c>[Transactional]</c> operation that writes through a sibling mutation and has a counted
///     external effect.
/// </summary>
/// <remarks>
///     <para>
///         With retry on, the transactional unit runs inside the execution strategy, and a transient
///         fault reruns it whole: the transaction, the body, and therefore the external effect too. The
///         rule is that a body has no effects outside its own transaction — mail, messages and events go
///         through the outbox, which is transactional.
///     </para>
///     <para>
///         The control case measures the happy path: the body runs <b>once</b>, and the row is written.
///         Without it, «wrapped in the strategy» would be compatible with a body executed twice.
///     </para>
/// </remarks>
[DomainAction]
[Transactional]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/shipments/transactional")]
public partial class CreateShipmentTransactionallyAction : DomainAction<Guid>
{
    private ISalesInternalActions _sales = null!;
    private ExternalEffectCounter _effects = null!;

    public required string TrackingCode { get; init; }

    public required string Carrier { get; init; }

    /// <inheritdoc />
    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        _effects.Increment();

        var created = await _sales.CreateShipmentReturningId(TrackingCode, Carrier, ct).ConfigureAwait(false);

        return created.IsFailure
            ? Result<Guid, IError>.Failure(created.Error)
            : Result<Guid, IError>.Success(created.Value);
    }
}
