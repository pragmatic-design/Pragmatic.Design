using Pragmatic;

namespace Casework.Intake.Enums;

/// <summary>
///     Where an organisation is in its onboarding: being made ready, ready, or gone.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>This is the state of a distributed process, and it is deliberately not a saga's.</b>
///         Onboarding crosses two services, so it has a half-way point; the question is where that fact
///         lives. It lives here because the framework already reads it: <c>ITenantStore</c> answers with
///         <c>TenantState</c>, and <c>EnforceTenantState</c> refuses a request whose tenant is not
///         <c>Active</c>. A saga would hold the same fact in a second table, and the one that decides
///         whether a request is served would still be this one — so the saga would be a copy that can
///         disagree.
///     </para>
///     <para>
///         The values mirror <c>Pragmatic.MultiTenancy.TenantState</c>'s meaning without being it: the
///         framework's enum is the store's vocabulary, this one is the column. <c>Suspended</c> and
///         <c>Migrating</c> are not here because nothing in this example produces them.
///     </para>
/// </remarks>
[FastEnum]
public enum OrganisationState
{
    /// <summary>
    ///     Registered, and not yet able to work: one of the two services has not finished.
    /// </summary>
    /// <remarks>
    ///     A request arriving as this organisation is refused with 403 by the tenant middleware, which is
    ///     the whole reason the state is written down before the work starts rather than after it.
    /// </remarks>
    Provisioning,

    /// <summary>Both services have a database for it, and it may work.</summary>
    Active,

    /// <summary>It may no longer be used. Its rows are still there.</summary>
    Deactivated
}
