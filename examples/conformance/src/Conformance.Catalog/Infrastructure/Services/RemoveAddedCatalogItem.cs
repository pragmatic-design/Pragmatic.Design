using Conformance.Catalog.Entities;
using Pragmatic.Actions.Compensation;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;

namespace Conformance.Catalog.Infrastructure.Services;

/// <summary>
///     Removes the item <see cref="AddCatalogItemUndoablyMutation"/> created.
/// </summary>
/// <remarks>
///     <para>
///         A compensator is an ordinary scoped service, not an operation: it holds the repositories of
///         the boundary that owns the row and writes through them. The invoker that committed the
///         original work is the one that commits — here the method stages the deletion and nothing else,
///         and the generated code does the <c>SaveChanges</c>.
///     </para>
///     <para>
///         It receives <b>what the operation returned</b>: for a mutation it is the entity, already
///         written, so the undo holds the row to remove without having to read it again. The generator
///         writes the container registration from <c>[UndoWith&lt;T&gt;]</c>, not this module.
///     </para>
/// </remarks>
public sealed class RemoveAddedCatalogItem(IRepository<CatalogItem> items) : ICompensates<CatalogItem>
{
    public Task<VoidResult<IError>> Undo(CatalogItem committed, CancellationToken ct = default)
    {
        items.Remove(committed);

        return Task.FromResult(VoidResult<IError>.Success());
    }
}
