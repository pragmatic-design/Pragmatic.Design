using Pragmatic.Identity;
using Pragmatic.Result;

namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     The read permission a preload asks — <c>RequireReadPermission = true</c> on <c>[LoadEntity]</c> or
///     <c>[LoadEntities]</c> — called by a generated preparation hook before the rows are read.
/// </summary>
/// <remarks>
///     The decision is <see cref="PermissionAuthorizationFilter" />'s, not a copy of it: the same answer for
///     an anonymous caller (401) and a caller without the permission (403), and an internal call — one
///     operation invoking another through its boundary — is not asked, as it is not asked the operation's
///     own permission.
/// </remarks>
public static class PreloadAuthorization
{
    /// <summary>
    ///     Refuses a caller that does not hold every one of <paramref name="permissions" />.
    /// </summary>
    /// <param name="services">The invocation's service provider.</param>
    /// <param name="permissions">The read permissions of the entities the operation preloads.</param>
    /// <param name="operation">The operation, as the refusal names it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refusal, or <see langword="null" /> when the caller may read the rows.</returns>
    public static async Task<IError?> RequireAllAsync(
        IServiceProvider services, string[] permissions, Type operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(operation);

        if (services.GetService(typeof(global::Pragmatic.Pipeline.ICallContext)) is global::Pragmatic.Pipeline.ICallContext { IsInternalCall: true })
            return null;

        var caller = services.GetService(typeof(ICurrentUser)) as ICurrentUser ?? AnonymousUser.Instance;
        var decision = await PermissionAuthorizationFilter.CheckRequirementAsync(
            new PermissionAuthorizationFilter.PermissionRequirement(permissions, PermissionAuthorizationFilter.PermissionMode.All),
            operation, caller, ct).ConfigureAwait(false);

        return decision.IsFailure ? decision.Error : null;
    }
}
