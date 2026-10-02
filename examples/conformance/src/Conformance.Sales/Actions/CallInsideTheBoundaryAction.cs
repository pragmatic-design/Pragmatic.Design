using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     A Sales operation that invokes another one <b>of the same boundary</b> through its own module's
///     internal facade.
/// </summary>
/// <remarks>
///     <para>
///         The twin of <see cref="Conformance.Sales.Queries.CallAcrossTheBoundaryAction" />, and the whole
///         difference is here: that one crosses a boundary and injects another module's <b>public</b>
///         interface, this one stays inside and injects <c>ISalesInternalActions</c>.
///     </para>
///     <para>
///         ⚠️ The shape can <b>hang the container</b>. A facade that took one invoker per operation in
///         its constructor would include this action's invoker, whose constructor asks for the facade
///         again. Microsoft's container detects cycles by walking constructor <b>parameters</b>, so with
///         the invoker resolving the facade inside its own body the cycle would be invisible: no
///         exception, the request simply stops answering. The facade takes only the provider and
///         resolves at call time, and this route is what keeps that true.
///     </para>
///     <para>
///         The invoked operation is <see cref="Conformance.Sales.Queries.AcknowledgeOrderAction" />,
///         which answers <c>204</c> on an existing order and <c>NotFound</c> otherwise: two distinct
///         outcomes through the facade, so the route proves it went through it and not merely that it
///         did not die.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/orders/inside-boundary")]
public partial class CallInsideTheBoundaryAction : VoidDomainAction
{
    private ISalesInternalActions _sales = null!;

    /// <summary>The order to acknowledge, passed to the sibling operation.</summary>
    public required Guid OrderId { get; init; }

    /// <inheritdoc />
    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var result = await _sales.AcknowledgeOrder(OrderId, ct).ConfigureAwait(false);

        return result.IsFailure ? VoidResult<IError>.Failure(result.Error) : Success;
    }
}
