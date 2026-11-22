using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Query.Results;
using Showcase.Catalog.Properties.Queries;

namespace Showcase.Catalog.Properties.Endpoints;

/// <summary>
///     Dynamic grid search for properties using <see cref="PropertyGridFilter" />.
///     Demonstrates: [GridFilter] usage inside a manual endpoint — dynamic per-field operator
///     selection (contains/startsWith/endsWith), range filters, multi-sort, paging.
/// </summary>
/// <remarks>
///     Contrast with <c>SearchPropertiesQuery</c> (<c>[Query]</c>) which is source-generated
///     end-to-end and better for fixed REST filters. GridFilter is ideal when the UI needs
///     to select the filter operator at runtime (e.g. AG Grid, DevExpress DataGrid).
/// </remarks>
[Endpoint(HttpVerb.Post, "/grid")]
[EndpointGroup<PropertiesGroup>]
[ApiSummary("Grid Search Properties")]
[ApiTags("Properties")]
[RequirePermission("catalog.property.read")]
public partial class GridSearchPropertiesEndpoint : Endpoint<PagedResult<PropertySummaryDto>>
{
    private IReadRepository<Property> _properties = null!;

    [FromBody]
    public required PropertyGridFilter Filter { get; init; }

    public override async Task<Result<PagedResult<PropertySummaryDto>>> HandleAsync(CancellationToken ct = default)
    {
        // No .Where(PropertySpecifications.IsActive()) here: [VisibleWhen<ActiveOnly>] on the entity
        // says it once, for every read. Repeated at each call site, it would be the same rule written
        // down twice and enforced wherever somebody remembered.
        var baseQuery = _properties.Query().AsNoTracking();

        // The total counts the filters alone (ToSpecification: no sort, no paging). Apply pages already, so the
        // rows are its result as it is: counted, it was one page; paged again, the second page was empty.
        var totalCount = await baseQuery.Where(Filter.ToSpecification().ToExpression()).CountAsync(ct).ConfigureAwait(false);

        var items = await Filter.Apply(baseQuery)
            .Select(PropertySummaryDto.Projection!)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return PagedResult<PropertySummaryDto>.Success(items, totalCount, Filter.Page, Filter.PageSize);
    }
}
