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
///     A <c>POST</c> that creates nothing, and to which the framework answers <c>201</c>.
/// </summary>
/// <remarks>
///     <para>
///         It exists to pin the result translation: there is no <c>[CreatedAt]</c> here and the response
///         is <c>201</c> anyway, because for an operation that is not a mutation the rule looks only at
///         the verb.
///     </para>
///     <para>
///         ⚠️ The case is documented, not endorsed. A count is not a created resource, and a <c>201</c>
///         without <c>Location</c> tells a client something it cannot follow. The test records it as
///         <b>current behaviour</b>: if it ever becomes <c>200</c>, that test will call for an account,
///         instead of leaving a gap between code and documentation.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/orders/{id}/line-count")]
public partial class CountOrderLinesAction : DomainAction<int, IError>
{
    private IRepository<Order> _orders = null!;

    public required Guid Id { get; init; }

    public override async Task<Result<int, IError>> Execute(CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(Id, ct).ConfigureAwait(false);
        if (order is null)
            return NotFoundError.For<Guid>(nameof(Order), Id);

        if (_orders is INavigationLoader<Order> loader)
            await loader.EnsureLoadedAsync(order, ["Lines"], ct).ConfigureAwait(false);

        return order.Lines.Count;
    }
}
