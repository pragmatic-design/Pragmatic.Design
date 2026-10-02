using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     Action filter that enforces <see cref="RequirePermissionAttribute" /> and
///     <see cref="RequireAnyPermissionAttribute" /> on domain actions and mutations.
///     Runs at Order 200 (after validation at 100, before transaction at 300).
/// </summary>
public sealed partial class PermissionAuthorizationFilter(
    IServiceProvider serviceProvider,
    ILogger<PermissionAuthorizationFilter> logger,
    IPermissionRequirementRegistry permissionRegistry) : IActionFilter
{
    // Static cache: PermissionRequirement metadata is keyed by Type and immutable once resolved,
    // so sharing it across DI scopes is safe and avoids re-parsing attributes on every resolution.
    private static readonly ConcurrentDictionary<Type, PermissionRequirement?> _requirementCache = new();
    private ICurrentUser? _currentUser;
    private ICurrentUser CurrentUser => _currentUser ??= serviceProvider.GetService(typeof(ICurrentUser)) as ICurrentUser
                                                         ?? AnonymousUser.Instance;
    private global::Pragmatic.Pipeline.ICallContext? _callContext;
    /// <remarks>
    ///     Resolved through <c>ICallContext</c>, not the concrete type. Every writer — the event
    ///     dispatcher, the message bus, the generated boundary invokers — goes through the interface,
    ///     so a reader that went around it would leave an <c>ICallContext</c> substituted in DI
    ///     written to by the framework and read by nobody.
    /// </remarks>
    private global::Pragmatic.Pipeline.ICallContext CallContext =>
        _callContext ??= serviceProvider.GetService(typeof(global::Pragmatic.Pipeline.ICallContext))
            as global::Pragmatic.Pipeline.ICallContext
            ?? new ActionCallContext();

    /// <inheritdoc />
    public int Order => Filters.FilterOrder.Authorization;

    /// <inheritdoc />
    public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(
        TAction action, CancellationToken ct)
        where TAction : DomainAction<TReturn>
        => CheckPermissionsAsync(typeof(TAction), ct);

    /// <inheritdoc />
    public Task AfterExecuteAsync<TAction, TReturn>(
        TAction action, Result<TReturn, IError> result, CancellationToken ct)
        where TAction : DomainAction<TReturn>
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<VoidResult<IError>> BeforeExecuteVoidAsync<TAction>(
        TAction action, CancellationToken ct)
        where TAction : VoidDomainAction
        => CheckPermissionsAsync(typeof(TAction), ct);

    /// <inheritdoc />
    public Task AfterExecuteVoidAsync<TAction>(
        TAction action, VoidResult<IError> result, CancellationToken ct)
        where TAction : VoidDomainAction
        => Task.CompletedTask;

    private async Task<VoidResult<IError>> CheckPermissionsAsync(Type actionType, CancellationToken ct)
    {
        var result = await CheckPermissionsForAsync(
            actionType, CurrentUser, CallContext.IsInternalCall, permissionRegistry, ct).ConfigureAwait(false);
        if (result.IsSuccess)
            LogPermissionGranted(actionType.Name);
        else if (!CurrentUser.IsAuthenticated)
            LogUnauthenticated(actionType.Name);
        else
            LogPermissionDenied(actionType.Name, result.Error.Title);

        return result;
    }

    /// <summary>
    ///     Checks permission requirements for any type (action or mutation).
    ///     Reusable from both DomainActionInvoker and MutationInvoker pipelines.
    /// </summary>
    internal static VoidResult<IError> CheckPermissionsFor(
        Type type, ICurrentUser currentUser, bool isInternalCall,
        IPermissionRequirementRegistry registry)
    {
        if (isInternalCall)
            return VoidResult<IError>.Success();

        var requirement = _requirementCache.GetOrAdd(type, t => ResolveRequirementFromRegistry(t, registry));

        if (requirement is null)
            return VoidResult<IError>.Success();

        if (!currentUser.IsAuthenticated)
            return VoidResult<IError>.Failure(
                UnauthorizedError.Create("Authentication required"));

        if (requirement.Mode == PermissionMode.All)
        {
            if (!currentUser.Authorization.HasAllPermissions(requirement.Permissions))
            {
                return VoidResult<IError>.Failure(
                    ForbiddenError.MissingPermissions(requirement.Permissions, PermissionMatch.All, type.Name));
            }
        }
        else
        {
            if (!currentUser.Authorization.HasAnyPermission(requirement.Permissions))
            {
                return VoidResult<IError>.Failure(
                    ForbiddenError.MissingPermissions(requirement.Permissions, PermissionMatch.Any, type.Name));
            }
        }

        return VoidResult<IError>.Success();
    }

    /// <summary>
    ///     Async permission check — awaits the (DB/cache-backed) permission resolution instead of blocking
    ///     a thread-pool thread. Preferred from the async invoker pipeline; the sync overload remains for
    ///     callers that aren't on an async path.
    /// </summary>
    internal static Task<VoidResult<IError>> CheckPermissionsForAsync(
        Type type, ICurrentUser currentUser, bool isInternalCall,
        IPermissionRequirementRegistry registry, CancellationToken ct = default)
    {
        if (isInternalCall)
            return Task.FromResult(VoidResult<IError>.Success());

        var requirement = _requirementCache.GetOrAdd(type, t => ResolveRequirementFromRegistry(t, registry));

        return CheckRequirementAsync(requirement, type, currentUser, ct);
    }

    /// <summary>
    ///     Decides on a requirement that has already been found, wherever it was found.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Split out because an operation's requirement reaches this pipeline two ways. An action's
    ///         comes from the generated registry, keyed by type, because the filter that asks is generic
    ///         and does not know which action it is running. A query's is written into its own generated
    ///         invoker, because the generator that emits that invoker knows the permission the author
    ///         declared and has no reason to look it up again at runtime.
    ///     </para>
    ///     <para>
    ///         ⚠️ What must <b>not</b> differ is the decision: which mode means what, what an
    ///         unauthenticated caller gets, and what the refusal says. Two copies of that is how one
    ///         permission comes to mean two things depending on which door it was asked at.
    ///     </para>
    /// </remarks>
    internal static async Task<VoidResult<IError>> CheckRequirementAsync(
        PermissionRequirement? requirement, Type type, ICurrentUser currentUser,
        CancellationToken ct = default)
    {
        if (requirement is null)
            return VoidResult<IError>.Success();

        if (!currentUser.IsAuthenticated)
            return VoidResult<IError>.Failure(
                UnauthorizedError.Create("Authentication required"));

        if (requirement.Mode == PermissionMode.All)
        {
            if (!await currentUser.Authorization.HasAllPermissionsAsync(requirement.Permissions, ct).ConfigureAwait(false))
            {
                return VoidResult<IError>.Failure(
                    ForbiddenError.MissingPermissions(requirement.Permissions, PermissionMatch.All, type.Name));
            }
        }
        else
        {
            if (!await currentUser.Authorization.HasAnyPermissionAsync(requirement.Permissions, ct).ConfigureAwait(false))
            {
                return VoidResult<IError>.Failure(
                    ForbiddenError.MissingPermissions(requirement.Permissions, PermissionMatch.Any, type.Name));
            }
        }

        return VoidResult<IError>.Success();
    }

    private static PermissionRequirement? ResolveRequirementFromRegistry(
        Type actionType, IPermissionRequirementRegistry registry)
    {
        // Zero-reflection: permission metadata is resolved from SG-generated registry
        var entry = registry.GetRequirement(actionType);
        if (entry is null || entry.Permissions.Length == 0)
            return null;

        return new PermissionRequirement(entry.Permissions, entry.RequireAll ? PermissionMode.All : PermissionMode.Any);
    }

    /// <summary>A resolved requirement: the permissions, and whether all of them are needed.</summary>
    /// <remarks>
    ///     Internal rather than private since a generated query invoker declares one — it is the shape
    ///     <see cref="CheckRequirementAsync" /> decides on, and having one shape is the point.
    /// </remarks>
    internal sealed record PermissionRequirement(string[] Permissions, PermissionMode Mode);

    /// <summary>Whether every declared permission is required, or any one of them.</summary>
    internal enum PermissionMode { All, Any }

    // =========================================================================
    // Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Trace, Message = "Action {ActionName} has no permission requirements, skipping")]
    private partial void LogNoRequirement(string actionName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action {ActionName} denied: user is not authenticated")]
    private partial void LogUnauthenticated(string actionName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action {ActionName} denied: missing permissions [{Permissions}]")]
    private partial void LogPermissionDenied(string actionName, string permissions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Action {ActionName} permission check passed")]
    private partial void LogPermissionGranted(string actionName);
}
