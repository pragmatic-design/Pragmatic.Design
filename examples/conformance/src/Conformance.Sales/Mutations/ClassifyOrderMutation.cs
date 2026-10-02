using Conformance.Catalog.Contracts;
using Conformance.Catalog.Entities;
using Conformance.Catalog.Queries;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The write that enforces an invariant <b>of another boundary</b>: an order can be classified only
///     with the name of a category the Catalog knows.
/// </summary>
/// <remarks>
///     <para>
///         It injects <c>ICatalogReads</c> — the contract <c>[Published]</c> generates in the Catalog
///         compilation — and never the module. There is no hand-written registration: that the contract
///         resolves here is what <c>TheContractAcrossTheBoundary</c> measures, because it is exactly what
///         would be missing if the host did not call <c>AddCatalogReads()</c>.
///     </para>
///     <para>
///         ⚠️ The field written is <c>Notes</c>, which already existed. The case demonstrates the
///         direction of the dependency, not a domain: adding a column would have changed the schema
///         without adding anything to the cell under test.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/orders/{id}/classification")]
[ReturnsDto<OrderDto>]
public partial class ClassifyOrderMutation : Mutation<Order>
{
    private ICatalogReads _catalog = null!;

    public required Guid Id { get; init; }

    /// <summary>The category's name, which lives across the boundary.</summary>
    /// <remarks>
    ///     <c>[MapIgnore]</c> because it is not a property of the order: it is the input of the check,
    ///     and what ends up on the entity is decided by <see cref="ApplyAsync" />.
    /// </remarks>
    [MapIgnore]
    public required string CategoryName { get; init; }

    public override async Task<Result<Order, IError>> ApplyAsync(Order entity, CancellationToken ct = default)
    {
        var known = await _catalog
            .SearchCategories(new SearchCategoriesQuery { Name = CategoryName }, ct)
            .ConfigureAwait(false);

        if (known.Count == 0)
            return NotFoundError.For<string>(nameof(Category), CategoryName);

        entity.SetNotes(CategoryName);
        return entity;
    }
}
