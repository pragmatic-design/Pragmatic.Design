using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The caller of <c>ApplyPatch</c>: written by hand, because no generated one exists.
/// </summary>
/// <remarks>
///     <para>
///         It is the reason a <c>[Patch&lt;T&gt;]</c> publishes <c>WrittenNavigations</c>. A mutation
///         invoker loads what it writes; here there is no invoker, and without the list this action would
///         have to guess what to include — or include nothing.
///     </para>
///     <para>
///         ⚠️ The two lines that matter are next to each other: the list comes <b>from the type</b>, and
///         the loading from the repository's capability. Neither knows about the other, and that is the
///         point: the first says what is needed, the second provides it, and the cost is 0 reads if the
///         graph is already complete.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Patch, "api/orders/{id}")]
public partial class ApplyOrderPatchAction : DomainAction<OrderDto, IError>
{
    private IRepository<Order> _orders = null!;

    public required Guid Id { get; init; }

    public required UpdateOrderPatch Patch { get; init; }

    public override async Task<Result<OrderDto, IError>> Execute(CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(Id, ct).ConfigureAwait(false);
        if (order is null)
            return NotFoundError.For<Guid>(nameof(Order), Id);

        // What the patch writes, asked of the patch; and loaded by whoever has the context.
        if (_orders is INavigationLoader<Order> loader)
            await loader.EnsureLoadedAsync(order, UpdateOrderPatch.WrittenNavigations, ct)
                .ConfigureAwait(false);

        Patch.ApplyPatch(order);

        return OrderDto.FromEntity(order);
    }
}
