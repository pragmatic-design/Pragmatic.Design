namespace Pragmatic.Identity;

/// <summary>
///     How the authority of a delegated session is composed from the subject's and the actor's.
/// </summary>
/// <remarks>
///     Chosen <b>per delegation</b>, not once per deployment, with the configured value as the
///     default. That is not a convenience: with a single deployment-wide policy the most common case
///     of all — a background job acting for the owner of a row — has no authority at all, because the
///     job's service identity holds no user-level permissions and the intersection is empty.
/// </remarks>
public enum DelegationPolicy
{
    /// <summary>
    ///     Subject ∩ actor. The safe default: an actor can do neither more than the person it acts
    ///     for, nor more than it is itself allowed.
    /// </summary>
    Intersection = 0,

    /// <summary>
    ///     The subject's authority, whole. Classic impersonation — support seeing the application as
    ///     the customer sees it, limitations included.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The actor gets everything the subject can do. The never-delegable list is the only thing
    ///     standing between it and changing the subject's password, so that list is not optional here.
    /// </remarks>
    SubjectOnly = 1,

    /// <summary>
    ///     Subject ∩ the permissions the grant names. Explicit least privilege: the delegation allows
    ///     three operations, not "everything I happen to have too".
    /// </summary>
    /// <remarks>The answer for a job or a service, whose own permission set is not user-shaped.</remarks>
    GrantScoped = 2,
}
