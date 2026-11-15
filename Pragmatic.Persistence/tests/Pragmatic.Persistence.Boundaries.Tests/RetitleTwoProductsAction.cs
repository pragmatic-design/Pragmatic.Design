using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Renames two products in one commit, a composite of two <see cref="RetitleProductMutation" /> steps: the generated composite invoker
///     owns the commit, and the steps' declared events wait in its batch (#800).
/// </summary>
[DomainAction]
[CompositeAction]
[BelongsTo<CatalogBoundary>]
public partial class RetitleTwoProductsAction : VoidDomainAction
{
    public required RetitleProductMutation First { get; init; }

    public required RetitleProductMutation Second { get; init; }
}
