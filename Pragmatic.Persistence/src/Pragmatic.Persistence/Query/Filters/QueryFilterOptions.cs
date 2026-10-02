namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Options controlling how <see cref="DefaultQueryFilterProvider" /> behaves.
/// </summary>
public sealed class QueryFilterOptions
{
    /// <summary>
    ///     When <see langword="true" /> (the default), permission-based row filters fail CLOSED
    ///     (the query returns no rows) instead of being skipped when no authenticated
    ///     user is present in <c>Normal</c> mode.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Secure-by-default: a broken or missing identity-propagation path
    ///         (misconfigured scope, a filter that expected a user that never arrived) returns nothing
    ///         rather than leaking every row cross-user/cross-scope. This only affects entities that
    ///         actually have a permission-based filter (<c>[HasOwner]</c>/<c>[HasAccessScopes]</c>/custom
    ///         <see cref="IPermissionBasedFilter"/>); entities with no such filter — genuinely public
    ///         data — are unaffected and remain readable anonymously.
    ///     </para>
    ///     <para>
    ///         Trusted elevated modes (<c>Admin</c>/<c>Background</c>/<c>Raw</c>) still
    ///         bypass as before. To restore the historical fail-open behavior — e.g. an
    ///         <c>[AllowAnonymous]</c> endpoint that intentionally exposes a permission-filtered entity
    ///         — register the option explicitly:
    ///         <c>services.AddSingleton(new QueryFilterOptions { FailClosedWhenAnonymous = false });</c>.
    ///     </para>
    /// </remarks>
    public bool FailClosedWhenAnonymous { get; set; } = true;
}
