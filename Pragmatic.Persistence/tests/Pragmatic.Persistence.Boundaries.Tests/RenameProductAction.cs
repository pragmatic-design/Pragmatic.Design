using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authoring;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Renames a product of the catalog, whose boundary has an event outbox, and declares <see cref="ProductRenamed" />: the operation's
///     event, which the invoker used to dispatch in process after the commit, outside the outbox (#736).
/// </summary>
[DomainAction]
[BelongsTo<CatalogBoundary>]
[Raises<ProductRenamed>]
[LoadEntity<Product>(nameof(Id))]
public partial class RenameProductAction : VoidDomainAction<NotFoundError>
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        _product.Name = Name;
        return Task.FromResult<VoidResult<IError>>(Success);
    }
}
