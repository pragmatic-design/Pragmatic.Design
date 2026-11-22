using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Results;
using Showcase.Catalog.Infrastructure.Grids;

namespace Showcase.Catalog.Properties.Endpoints;

/// <summary>
///     Dynamic grid search for properties in DevExpress's own LoadOptions format, translated by the
///     adapter the generator wrote from <see cref="ThePropertyGrid" />.
/// </summary>
/// <remarks>
///     <para>
///         Contrast with <c>GridSearchPropertiesEndpoint</c> (<c>[GridFilter]</c>), which uses a typed
///         DTO of this application's own design. This one takes the format the grid already speaks, so
///         the client contract is the grid vendor's and nothing is translated by hand in between.
///     </para>
///     <para>
///         ⚠️ <b>It builds the query with the generated adapter, not with <c>DevExpressAdapter</c>.</b>
///         The runtime adapter resolves a field name with <c>Expression.Property</c> — reflection, on
///         the request path — and reaches any public property of the entity, because a name is all it
///         has. The generated adapter is a <c>switch</c> over the columns <see cref="ThePropertyGrid" />
///         declares: same request, same response, and <c>TenantId</c> unreachable because it is
///         excluded there.
///     </para>
///     <para>
///         HTTP usage:
///         <code>
///         POST /api/properties/devexpress
///         {
///           "filter": [["name", "contains", "Rome"], "and", ["stars", "&gt;=", 3]],
///           "sort":   [{ "selector": "name", "desc": false }],
///           "skip":   0,
///           "take":   20
///         }
///         </code>
///         ⚠️ <c>"stars"</c>, not <c>"starRating"</c>: the grid's own name for that column, declared
///         once in <see cref="ThePropertyGrid" />.
///     </para>
/// </remarks>
[Endpoint(HttpVerb.Post, "/devexpress")]
[EndpointGroup<PropertiesGroup>]
[ApiSummary("DevExpress Grid Search Properties")]
[ApiTags("Properties")]
[RequirePermission("catalog.property.read")]
public partial class DevExpressSearchPropertiesEndpoint : Endpoint<PagedResult<PropertySummaryDto>>
{
    private IReadRepository<Property> _properties = null!;

    [FromBody]
    public required DevExpressLoadOptions LoadOptions { get; init; }

    public override async Task<Result<PagedResult<PropertySummaryDto>>> HandleAsync(CancellationToken ct = default)
    {
        // A grid that sends no Take gets a page of twenty, as this endpoint always answered.
        var options = LoadOptions.Take is not null
            ? LoadOptions
            : new DevExpressLoadOptions { Filter = LoadOptions.Filter, Sort = LoadOptions.Sort, Skip = LoadOptions.Skip, Take = 20 };

        // The active-only filter is gone from here too — the entity carries it. What is left is what
        // this endpoint is actually about: an external grid format, translated by generated code.
        //
        // LoadAsync reads the page through the repository with the filter's total — the count without
        // the page, which is what the grid puts under its pager — and compiles on every engine the
        // entity can be persisted on (#947): an IQueryable is EF Core's alone.
        var page = await ThePropertyGrid.LoadAsync(_properties, options, ct).ConfigureAwait(false);

        return PagedResult<PropertySummaryDto>.Success([.. page.Items.Select(PropertySummaryDto.Selector)], page.TotalCount, page.Page, page.PageSize);
    }
}
