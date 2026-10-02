namespace Showcase.Catalog.Properties.Queries;

/// <summary>
///     The one read that sees deactivated properties, for whoever manages the catalogue.
/// </summary>
/// <remarks>
///     <para>
///         <c>[VisibleWhen&lt;ActiveOnly&gt;]</c> on <c>Property</c> takes deactivated rows out of
///         every query. This lifts that one rule — not the entity's whole filter set, so soft-delete
///         and the tenant filter still apply: a manager sees what was switched off, not what was
///         deleted and not another tenant's.
///     </para>
///     <para>
///         The permission is what makes the lift legitimate, and <c>PRAG0719</c> refuses the attribute
///         without one. <c>Update</c> rather than <c>Read</c> because deciding what is in the
///         catalogue is the same authority as changing it — reading a row the catalogue hides is not
///         ordinary reading.
///     </para>
/// </remarks>
[Query<Property, PropertySummaryDto>]
[Endpoint(HttpVerb.Get, "api/properties/deactivated")]
[WithoutFilter<ActiveOnly>]
[RequirePermission(CatalogPermissions.Property.Update)]
public partial class SearchDeactivatedPropertiesQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }

    [Filter]
    public string? City { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? NameSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
