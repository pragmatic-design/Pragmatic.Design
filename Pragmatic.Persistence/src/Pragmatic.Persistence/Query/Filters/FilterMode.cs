namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Determines which categories of filters are active.
///     Used by <see cref="FilterContext"/> to control filter behavior without magic strings.
/// </summary>
public enum FilterMode
{
    /// <summary>
    ///     All filters active — SoftDelete, Tenant, Visibility, Permission-based.
    ///     Default for authenticated user requests.
    /// </summary>
    Normal = 0,

    /// <summary>
    ///     Skip permission-based filters — which includes ownership and data scopes — keeping
    ///     SoftDelete and Tenant. Use for admin operations that should see all data within the tenant.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Ownership and scopes go with it.</b> The generated <c>OwnershipFilter</c> and
    ///         <c>ScopedDataFilter</c> are both <c>IPermissionBasedFilter</c>, and this drops every
    ///         filter carrying that marker — so an <c>Admin</c> read sees other owners' rows. There is no
    ///         separate step that lifts scopes on their own; <c>FilterModeLadderTests</c> measures what
    ///         each mode lifts.
    ///     </para>
    ///     <para>
    ///         A declared <c>[VisibleWhen&lt;TRule&gt;]</c> rule is <b>not</b> lifted here. It is an EF
    ///         Core named query filter, and only <c>Raw</c> — or naming it — gets past one.
    ///     </para>
    /// </remarks>
    Admin = 1,

    // 2 is retired and not reused: it was indistinguishable from Admin. The values above it keep
    // their numbers so a compiled [FilterMode(3)] still means Background.

    /// <summary>
    ///     Everything <see cref="Admin" /> lifts, plus tenant filters. Keep SoftDelete only.
    ///     Use for background jobs that operate across tenants but respect soft-delete.
    /// </summary>
    Background = 3,

    /// <summary>
    ///     No automatic filters applied. The FilterMap is empty.
    ///     Use explicitly and carefully — bypasses all safety nets.
    /// </summary>
    Raw = 4
}
