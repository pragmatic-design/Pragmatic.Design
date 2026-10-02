using Conformance.Catalog.Infrastructure.Services;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Validation.Attributes;

namespace Conformance.Catalog.Entities;

/// <summary>
///     A catalog operation that <b>can undo itself</b>: whoever invokes it from another boundary does
///     not leave the item behind if it then fails.
/// </summary>
/// <remarks>
///     <para>
///         The boundary is the transaction boundary: the catalog saves on its own, first, and a later
///         failure of the caller does not touch it. That is what <c>PRAG0424</c> asks to decide, and here
///         the decision is <c>[UndoWith&lt;T&gt;]</c> instead of the <c>[AcceptsPartialWrites]</c> that
///         the two siblings next to it declare: there the two writes have no invariant between them,
///         here an item created for an order that does not exist is garbage.
///     </para>
///     <para>
///         ⚠️ <b>Best effort and within the request, and not a saga.</b> A crash between the inner
///         commit and the compensation leaves the item written: nothing here is durable and nothing is
///         retried. The case measures what the attribute really promises — a failure that
///         <em>returns</em> — not the row that survives a process that dies.
///     </para>
///     <para>
///         <c>Internal = false</c> because Sales must be able to invoke it: the boundary's public
///         interface carries the <c>[CompensableStep]</c> the generator writes from this declaration,
///         and reading it is how the caller knows the step undoes itself.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Create, Internal = false)]
// The case measures compensation, not permission: that has its own cells, and this module's posture
// would impose a derived one that has nothing to do with what is being looked at.
[AllowAnonymous]
[UndoWith<RemoveAddedCatalogItem>]
public partial class AddCatalogItemUndoablyMutation : Mutation<CatalogItem>
{
    [Required]
    public required string Name { get; init; }

    public decimal ListPrice { get; init; }
}
