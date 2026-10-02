using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     Action filter that enforces resource-level authorization via
///     <see cref="IResourceAuthorizer{TResource}"/>. Runs at Order 250
///     (after permission check at 200, before transaction at 300).
/// </summary>
/// <remarks>
///     <para>
///         <b>This filter has no internal-call bypass, and that is deliberate.</b> The filters at 200
///         and 210 skip when <c>ICallContext.IsInternalCall</c> is set; this one runs either way.
///     </para>
///     <para>
///         The asymmetry is the point. A permission answers "may this user perform this kind of
///         operation", and when one action calls another through a boundary interface that question
///         was already answered at the outer call — asking it again at every hop would make an
///         action's own permission the union of everything it might reach. Resource authorization
///         answers a different question: "may this user touch <i>this instance</i>". The outer
///         permission says nothing about it, so entering internal-call mode is not evidence that
///         anyone checked. Skipping here would let any action reach any instance by calling through
///         another action.
///     </para>
///     <para>
///         Note that the user does not disappear in internal-call mode: the mode is per-scope and
///         <c>ICurrentUser</c> is still the caller. A background job that has no user gets
///         <c>AnonymousUser</c> — but it would get that with or without this filter's bypass, so the
///         bypass would not fix it. Give such a job an authorizer that admits its principal.
///     </para>
/// </remarks>
public sealed partial class ResourceAuthorizationFilter(
    IServiceProvider serviceProvider,
    ILogger<ResourceAuthorizationFilter> logger) : IActionFilter
{
    // Per-instance cache scoped to this provider — avoids cross-container fail-open
    private readonly ConcurrentDictionary<Type, bool> _hasAuthorizerCache = new();

    // For the types with no authorizer of their own: the nearest base type that does have one, or null.
    // Same per-instance scoping, same reason.
    private readonly ConcurrentDictionary<Type, Type?> _coveringBaseCache = new();
    private ICurrentUser? _currentUser;
    private ICurrentUser CurrentUser => _currentUser ??= serviceProvider.GetService(typeof(ICurrentUser)) as ICurrentUser
                                                         ?? AnonymousUser.Instance;

    /// <inheritdoc />
    public int Order => Filters.FilterOrder.ResourceAuthorization;

    /// <inheritdoc />
    public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(
        TAction action, CancellationToken ct)
        where TAction : DomainAction<TReturn>
        => CheckResourceAuthorization(action, ct);

    /// <inheritdoc />
    public Task AfterExecuteAsync<TAction, TReturn>(
        TAction action, Result<TReturn, IError> result, CancellationToken ct)
        where TAction : DomainAction<TReturn>
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<VoidResult<IError>> BeforeExecuteVoidAsync<TAction>(
        TAction action, CancellationToken ct)
        where TAction : VoidDomainAction
        => CheckResourceAuthorization(action, ct);

    /// <inheritdoc />
    public Task AfterExecuteVoidAsync<TAction>(
        TAction action, VoidResult<IError> result, CancellationToken ct)
        where TAction : VoidDomainAction
        => Task.CompletedTask;

    /// <summary>
    ///     Mutation-pipeline entry point. The <see cref="IActionFilter"/> methods are constrained to
    ///     <see cref="DomainAction{TReturn}"/>/<see cref="VoidDomainAction"/>, but mutations need the same
    ///     resource authorization. The core check is unconstrained, so the mutation invoker calls this with
    ///     the mutation instance (the "resource" inspected by <see cref="IResourceAuthorizer{T}"/>).
    /// </summary>
    internal Task<VoidResult<IError>> AuthorizeResourceAsync<T>(T target, CancellationToken ct)
        => CheckResourceAuthorization(target, ct);

    private async Task<VoidResult<IError>> CheckResourceAuthorization<TAction>(
        TAction action, CancellationToken ct)
    {
        var actionType = typeof(TAction);
        var authorizerType = typeof(IResourceAuthorizer<TAction>);

        // GetOrAdd ensures atomic lookup+store, eliminating the TOCTOU race where two
        // concurrent calls could both resolve and cache conflicting results.
        var hasAuthorizer = _hasAuthorizerCache.GetOrAdd(
            actionType,
            _ => serviceProvider.GetService(authorizerType) is not null);

        if (!hasAuthorizer)
            return ResolveUncovered(actionType);

        var authorizer = serviceProvider.GetService(authorizerType) as IResourceAuthorizer<TAction>;

        if (authorizer is null)
            return ResolveUncovered(actionType);

        var allowed = await authorizer.CanAccessAsync(
            CurrentUser, action, actionType.Name, ct).ConfigureAwait(false);

        if (!allowed)
        {
            LogResourceDenied(actionType.Name, CurrentUser.Id);
            return VoidResult<IError>.Failure(
                ForbiddenError.ActionDenied(actionType.Name));
        }

        LogResourceGranted(actionType.Name);
        return VoidResult<IError>.Success();
    }

    /// <summary>
    ///     Decides what "no authorizer for this exact type" means.
    ///     <para>
    ///         Resource authorization is opt-in: most actions have none by design, and for them success
    ///         is the right answer — denying them would not close a hole, it would break the design.
    ///     </para>
    ///     <para>
    ///         But an authorizer registered for a <i>base</i> of this action is a different situation.
    ///         The author asked for this hierarchy to be protected, and the container will never hand
    ///         that authorizer over for the derived type: it keys registrations by the closed generic
    ///         and applies no variance. Passing there is a silent fail-open, so it fails closed instead.
    ///     </para>
    /// </summary>
    private VoidResult<IError> ResolveUncovered(Type actionType)
    {
        var coveringBase = _coveringBaseCache.GetOrAdd(actionType, FindCoveringBase);

        if (coveringBase is not null)
        {
            LogBaseAuthorizerNotApplicable(actionType.Name, coveringBase.Name, CurrentUser.Id);
            return VoidResult<IError>.Failure(
                ForbiddenError.ActionDenied(actionType.Name, resource: coveringBase.Name));
        }

        LogNoAuthorizer(actionType.Name);
        return VoidResult<IError>.Success();
    }

    /// <summary>
    ///     Walks the base chain of <paramref name="actionType"/> looking for a type the authorizer
    ///     catalog covers. Plain <see cref="Type.BaseType"/> navigation — no constructed generic, so
    ///     nothing here needs <c>MakeGenericType</c> and the whole path stays AOT-safe.
    /// </summary>
    private Type? FindCoveringBase(Type actionType)
    {
        if (serviceProvider.GetService(typeof(IResourceAuthorizerCatalog)) is not IResourceAuthorizerCatalog catalog)
            return null;

        for (var baseType = actionType.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (catalog.Covers(baseType))
                return baseType;
        }

        return null;
    }

    // =========================================================================
    // Logging
    // =========================================================================

    // Debug, not Trace: this line records that an authorization check was skipped. At Trace it was
    // invisible in every realistic configuration.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Action {ActionName} has no resource authorizer, skipping")]
    private partial void LogNoAuthorizer(string actionName);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Action {ActionName} denied for user {UserId}: an IResourceAuthorizer is registered for its base type {BaseTypeName}, but dependency injection resolves authorizers by exact type and will never apply it to {ActionName}. Register an authorizer for {ActionName} itself.")]
    private partial void LogBaseAuthorizerNotApplicable(string actionName, string baseTypeName, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action {ActionName} resource authorization denied for user {UserId}")]
    private partial void LogResourceDenied(string actionName, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Action {ActionName} resource authorization passed")]
    private partial void LogResourceGranted(string actionName);
}
