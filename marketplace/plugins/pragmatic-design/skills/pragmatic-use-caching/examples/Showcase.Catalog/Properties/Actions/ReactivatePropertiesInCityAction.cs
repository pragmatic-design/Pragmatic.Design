using Showcase.Catalog.Properties.Queries;

namespace Showcase.Catalog.Properties.Actions;

/// <summary>
/// Switches every deactivated property of a city back on.
/// Demonstrates: the fourth rung of the read ladder — a declared [Query] run through the repository
/// the operation already holds, with <c>repository.RunAsync(query, ct)</c>.
/// </summary>
/// <remarks>
///     <para>
///         The read is declared **once**: this is the same <c>SearchDeactivatedPropertiesQuery</c> the
///         <c>api/properties/deactivated</c> route answers with, filters, sort and
///         <c>[WithoutFilter&lt;ActiveOnly&gt;]</c> included. Without <c>RunAsync</c> an operation had
///         to resolve <c>IQueryExecutor</c>, obtain a raw entity set and pick an overload — so the
///         realistic thing to do was to write the LINQ again by hand, and the declared query stayed
///         reachable only from its own route.
///     </para>
///     <para>
///         ⚠️ No operation pipeline runs inside <c>RunAsync</c>, and that is right here: this action
///         has its own validation, permission and transaction from its invoker. Invoking the query as
///         an operation — with its own permission checked again — is the boundary facade's job, and
///         this is not that.
///     </para>
/// </remarks>
[DomainAction]
// ⚠️ The same lift the query carries, and the action needs it too: the query finds a deactivated row,
// and loading it to write goes through the repository's ordinary filters, which hide it. Without this
// the operation reads what it cannot then act on — measured, it reactivated nothing.
[WithoutFilter<ActiveOnly>]
// The same tag UpdatePropertyMutation drops, for the same reason: this writes IsActive on rows that
// api/properties/search answers with, and that route is [Cacheable(Duration = "5m", Tags =
// ["properties"])]. Without it the reactivation is committed and invisible for five minutes.
// ⚠️ The first [InvalidatesCache] on an action in either application: an action's invalidation
// runs exactly like a mutation's.
[InvalidatesCache("properties")]
[RequirePermission(CatalogPermissions.Property.Update)]
[Endpoint(HttpVerb.Post, "api/properties/reactivate-city")]
[ApiSummary("Reactivate a city's properties")]
[ApiTags("Properties")]
public partial class ReactivatePropertiesInCityAction : DomainAction<int>
{
    private IRepository<Property> _properties = null!;

    /// <summary>The city whose deactivated properties come back.</summary>
    public required string City { get; init; }

    public override async Task<Result<int, IError>> Execute(CancellationToken ct = default)
    {
        // The declared query, run against this repository's own set. A page of 100 because this is a
        // per-city correction and not a migration; the query's own paging decides the rest.
        var deactivated = await _properties
            .RunAsync(new SearchDeactivatedPropertiesQuery { City = City, PageSize = 100 }, ct)
            .ConfigureAwait(false);

        if (deactivated.IsFailure)
            return Result<int, IError>.Failure(deactivated.Error!);

        var reactivated = 0;

        // The query lifts the rule; it does not narrow to what the rule hides. Only the rows that are
        // actually off are switched back on.
        foreach (var summary in deactivated.Items.Where(p => !p.IsActive))
        {
            var property = await _properties.GetByIdAsync(summary.Id, ct).ConfigureAwait(false);
            if (property is null)
                continue;

            property.SetIsActive(true);
            reactivated++;
        }

        return reactivated;
    }
}
